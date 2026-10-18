using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MicCheck;

/// <summary>An active capture endpoint and which apps are recording from it right now.</summary>
internal sealed class CaptureDevice
{
    public required string Id;
    public required string Name;
    public required IMMDevice MMDevice;
    public required IAudioMeterInformation Meter;
    public required IAudioEndpointVolume Volume;
    public required IAudioSessionManager2 Sessions;
    public List<string> RecordingApps = new();
}

/// <summary>
/// Keeps the list of active capture endpoints and their recording apps up to date. Shared by every key.
/// Every COM call must happen on the same MTA thread.
/// </summary>
internal sealed class AudioDevices
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(1);

    private readonly IMMDeviceEnumerator enumerator = CoreAudio.CreateEnumerator();
    private readonly Dictionary<string, CaptureDevice> devices = new();
    private readonly Dictionary<uint, string> processNames = new();
    private readonly uint ourPid = (uint)Environment.ProcessId;
    private DateTime lastScan = DateTime.MinValue;

    public IReadOnlyCollection<CaptureDevice> All => devices.Values;
    public string? DefaultCommId { get; private set; }
    public CaptureDevice? DefaultComm => DefaultCommId != null ? Get(DefaultCommId) : null;
    public CaptureDevice? Get(string id) => devices.GetValueOrDefault(id);

    /// <summary>Rescans if it has been a second since the last scan.</summary>
    public void Refresh()
    {
        DateTime now = DateTime.UtcNow;
        if (now - lastScan < ScanInterval) return;
        lastScan = now;
        Scan();
    }

    private void Scan()
    {
        var seen = new HashSet<string>();
        if (enumerator.EnumAudioEndpoints(EDataFlow.Capture, DeviceState.Active, out IMMDeviceCollection coll) == 0)
        {
            coll.GetCount(out int count);
            for (int i = 0; i < count; i++)
            {
                if (coll.Item(i, out IMMDevice mm) != 0) continue;
                string id = CoreAudio.GetId(mm);
                seen.Add(id);
                if (!devices.TryGetValue(id, out CaptureDevice? d))
                {
                    try
                    {
                        d = new CaptureDevice
                        {
                            Id = id,
                            Name = CoreAudio.GetFriendlyName(mm),
                            MMDevice = mm,
                            Meter = CoreAudio.Activate<IAudioMeterInformation>(mm),
                            Volume = CoreAudio.Activate<IAudioEndpointVolume>(mm),
                            Sessions = CoreAudio.Activate<IAudioSessionManager2>(mm),
                        };
                        devices[id] = d;
                    }
                    catch (Exception ex) { Log.Write($"Could not open capture device {id}: {ex.Message}"); continue; }
                }
                d.RecordingApps = GetRecordingApps(d);
            }
        }
        foreach (string gone in devices.Keys.Where(k => !seen.Contains(k)).ToList())
            devices.Remove(gone);

        DefaultCommId = enumerator.GetDefaultAudioEndpoint(EDataFlow.Capture, ERole.Communications, out IMMDevice def) == 0 ? CoreAudio.GetId(def) : null;
    }

    private List<string> GetRecordingApps(CaptureDevice d)
    {
        var apps = new List<string>();
        if (d.Sessions.GetSessionEnumerator(out IAudioSessionEnumerator sessions) != 0) return apps;
        try
        {
            sessions.GetCount(out int count);
            for (int i = 0; i < count; i++)
            {
                if (sessions.GetSession(i, out IAudioSessionControl2 s) != 0) continue;
                try
                {
                    if (s.GetState(out AudioSessionState state) == 0 && state == AudioSessionState.Active
                        && s.GetProcessId(out uint pid) == 0 && pid != 0 && pid != ourPid)
                    {
                        string name = ProcessName(pid);
                        if (!apps.Contains(name)) apps.Add(name);
                    }
                }
                finally { CoreAudio.Release(s); }
            }
        }
        finally { CoreAudio.Release(sessions); }
        return apps;
    }

    private string ProcessName(uint pid)
    {
        if (processNames.TryGetValue(pid, out string? name)) return name;
        try { name = Process.GetProcessById((int)pid).ProcessName; }
        catch { name = "pid " + pid; }
        if (processNames.Count > 500) processNames.Clear();
        return processNames[pid] = name;
    }
}
