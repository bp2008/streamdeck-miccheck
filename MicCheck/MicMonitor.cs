using System.Runtime.InteropServices;

namespace MicCheck;

internal enum MicMode
{
    /// <summary>Nothing is recording from the watched mic.</summary>
    Idle,
    /// <summary>Some app (Teams, Discord, ...) is recording from the watched mic.</summary>
    InUse,
    /// <summary>The user pressed the key and we are recording briefly so the meter can show a level.</summary>
    Test,
    /// <summary>"Always listen" is on and no other app is recording, so we are recording to keep the meter live.</summary>
    Live,
    /// <summary>The chosen mic is not connected.</summary>
    Missing,
    /// <summary>A call app is recording from a different mic than the one this key watches.</summary>
    OtherMic,
}

internal sealed record MicSnapshot(
    MicMode Mode,
    string Label,
    string DeviceName,
    bool Muted,
    int VolumePercent,
    float Level,
    float[] History,
    bool NoSignal,
    int TestSecondsLeft,
    string OtherDevice = "");

/// <summary>
/// The meter for one Stream Deck key. Every COM call happens on the thread that calls <see cref="Sample"/> and
/// <see cref="Tick"/>, which must be the same MTA thread that owns the <see cref="AudioDevices"/>.
/// </summary>
internal sealed class MicMonitor : IDisposable
{
    public const int HistoryLength = 24;
    private static readonly TimeSpan NoSignalAfter = TimeSpan.FromSeconds(2);

    /// <summary>Process name → label shown on the key. These win when several apps are recording.</summary>
    private static readonly Dictionary<string, string> CallApps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ms-teams"] = "Teams",
        ["teams"] = "Teams",
        ["discord"] = "Discord",
        ["zoom"] = "Zoom",
        ["cpthost"] = "Zoom",
    };

    /// <summary>Friendlier labels for other common recorders, whose process names are long or cryptic.</summary>
    private static readonly Dictionary<string, string> OtherApps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SystemSettings"] = "Settings",
        ["rundll32"] = "Sound",
        ["obs64"] = "OBS",
        ["msedgewebview2"] = "WebView",
        ["msedge"] = "Edge",
        ["chrome"] = "Chrome",
        ["firefox"] = "Firefox",
        ["SoundRecorder"] = "Recorder",
        ["Slack"] = "Slack",
    };

    private readonly AudioDevices devices;
    private readonly float[] history = new float[HistoryLength];
    private MeterSettings settings;

    private string? targetId;
    private float framePeak;
    private DateTime? silentSince;

    private IAudioClient? listenClient;
    private IntPtr listenFormat;
    private string? listenDeviceId;
    private DateTime listenUntil;

    public MicMonitor(AudioDevices devices, MeterSettings settings)
    {
        this.devices = devices;
        this.settings = settings;
    }

    public MeterSettings Settings
    {
        get => settings;
        set
        {
            if (value == settings) return;
            settings = value;
            StopListening();
            targetId = null;
        }
    }

    /// <summary>Call often (about every 20 ms) so short peaks between frames are not missed.</summary>
    public void Sample()
    {
        if (targetId != null && devices.Get(targetId) is { } d && d.Meter.GetPeakValue(out float p) == 0 && p > framePeak)
            framePeak = p;
    }

    /// <summary>Call once per frame (about every 100 ms), after <see cref="AudioDevices.Refresh"/>.</summary>
    public MicSnapshot Tick(bool keyPressed)
    {
        DateTime now = DateTime.UtcNow;
        (CaptureDevice? target, MicMode mode, string label, string other) = PickTarget();

        // Our own recording, used for the press-to-test and "always listen" features
        if (listenClient != null && (listenDeviceId != target?.Id || now >= listenUntil)) StopListening();
        if (settings.AlwaysListen)
        {
            if (listenClient == null && target != null && mode is MicMode.Idle or MicMode.InUse) StartListening(target, DateTime.MaxValue);
        }
        else if (keyPressed)
        {
            if (listenClient != null) StopListening();
            else if (mode == MicMode.Idle && target != null) StartListening(target, now.AddSeconds(settings.TestSeconds));
        }
        if (listenClient != null && mode == MicMode.Idle)
        {
            mode = settings.AlwaysListen ? MicMode.Live : MicMode.Test;
            label = settings.AlwaysListen ? "LIVE" : "TEST";
        }

        if (target?.Id != targetId)
        {
            targetId = target?.Id;
            Array.Clear(history);
            silentSince = null;
            framePeak = 0;
            Sample();
        }

        bool muted = false;
        int volume = 100;
        if (target != null)
        {
            if (target.Volume.GetMute(out bool m) == 0) muted = m;
            if (target.Volume.GetMasterVolumeLevelScalar(out float v) == 0) volume = (int)Math.Round(v * 100);
        }

        bool metering = mode is MicMode.InUse or MicMode.Test or MicMode.Live;
        float level = metering ? ToMeterScale(framePeak) : 0;
        Array.Copy(history, 1, history, 0, HistoryLength - 1);
        history[HistoryLength - 1] = level;

        // A real microphone always picks up a little noise. Exact digital silence while something is recording
        // usually means a hardware mute switch, an unplugged mic, or a privacy block.
        bool noSignal = false;
        if (metering && framePeak == 0)
        {
            silentSince ??= now;
            noSignal = now - silentSince >= NoSignalAfter;
        }
        else silentSince = null;
        framePeak = 0;

        int testLeft = mode == MicMode.Test ? Math.Min((int)Math.Ceiling((listenUntil - now).TotalSeconds), settings.TestSeconds) : 0;
        string deviceName = target?.Name ?? (settings.DeviceName.Length > 0 ? settings.DeviceName : "No microphone");
        return new MicSnapshot(mode, label, ShortName(deviceName), muted, volume, level, (float[])history.Clone(), noSignal, testLeft, ShortName(other));
    }

    /// <summary>Bottom of the meter. Room noise on a typical headset mic is around -60 dB, so it still shows as a small bar.</summary>
    public const double FloorDb = -72;

    /// <summary>Level in dB → meter position (0..1) on a <see cref="FloorDb"/> .. 0 dB scale.</summary>
    public static float ToMeterScale(double db) => (float)Math.Clamp((db - FloorDb) / -FloorDb, 0, 1);

    private static float ToMeterScale(float peak) => peak <= 0 ? 0 : ToMeterScale(20 * Math.Log10(peak));

    /// <summary>Returns the mic to meter, what the key is doing, its top label, and (for OtherMic) the mic the call app is using.</summary>
    private (CaptureDevice?, MicMode, string, string) PickTarget()
    {
        if (settings.Device == MeterSettings.Auto)
        {
            // Follow whichever mic is being recorded: call apps first, then the default communications mic, then anything
            CaptureDevice? best = null;
            string bestLabel = "";
            int bestRank = -1;
            foreach (CaptureDevice d in devices.All)
                foreach (string app in d.RecordingApps)
                {
                    int rank = CallApps.ContainsKey(app) ? 2 : d.Id == devices.DefaultCommId ? 1 : 0;
                    if (rank > bestRank) { best = d; bestRank = rank; bestLabel = AppLabel(app); }
                }
            if (best != null) return (best, MicMode.InUse, bestLabel, "");
            return (devices.DefaultComm ?? devices.All.FirstOrDefault(), MicMode.Idle, "IDLE", "");
        }

        CaptureDevice? target = settings.Device == MeterSettings.DefaultComm ? devices.DefaultComm : devices.Get(settings.Device);
        if (target == null) return (null, MicMode.Missing, "NO MIC", "");

        string? onTarget = target.RecordingApps.OrderByDescending(a => CallApps.ContainsKey(a)).FirstOrDefault();
        if (onTarget != null) return (target, MicMode.InUse, AppLabel(onTarget), "");

        // Nothing is recording from the watched mic, but a call app is recording from another one: probably the wrong mic
        foreach (CaptureDevice d in devices.All)
            if (d != target && d.RecordingApps.FirstOrDefault(CallApps.ContainsKey) is { } callApp)
                return (target, MicMode.OtherMic, AppLabel(callApp), d.Name);

        return (target, MicMode.Idle, "IDLE", "");
    }

    private static string AppLabel(string processName) =>
        CallApps.TryGetValue(processName, out string? l) || OtherApps.TryGetValue(processName, out l) ? l : processName;

    private void StartListening(CaptureDevice d, DateTime until)
    {
        try
        {
            var client = CoreAudio.Activate<IAudioClient>(d.MMDevice);
            Marshal.ThrowExceptionForHR(client.GetMixFormat(out listenFormat));
            Marshal.ThrowExceptionForHR(client.Initialize(0 /* shared */, 0, 10_000_000 /* 1 s */, 0, listenFormat, IntPtr.Zero));
            Marshal.ThrowExceptionForHR(client.Start());
            listenClient = client;
            listenDeviceId = d.Id;
            listenUntil = until;
        }
        catch (Exception ex)
        {
            Log.Write("Could not start recording for the meter: " + ex.Message);
            StopListening();
            listenFailed = true;
        }
    }

    private bool listenFailed;

    /// <summary>True once after a press-to-test (or always-listen) recording failed to start, so the key can flash an alert.</summary>
    public bool TakeListenFailure()
    {
        bool failed = listenFailed;
        listenFailed = false;
        return failed;
    }

    private void StopListening()
    {
        if (listenClient != null)
        {
            try { listenClient.Stop(); } catch { }
            CoreAudio.Release(listenClient);
            listenClient = null;
        }
        if (listenFormat != IntPtr.Zero) { Marshal.FreeCoTaskMem(listenFormat); listenFormat = IntPtr.Zero; }
        listenDeviceId = null;
    }

    /// <summary>"Microphone (BlackShark V3 Pro - Chat)" → "BlackShark V3 Pro - Chat"</summary>
    public static string ShortName(string friendly)
    {
        int open = friendly.IndexOf('('), close = friendly.LastIndexOf(')');
        return open >= 0 && close > open ? friendly.Substring(open + 1, close - open - 1).Trim() : friendly;
    }

    public void Dispose() => StopListening();
}
