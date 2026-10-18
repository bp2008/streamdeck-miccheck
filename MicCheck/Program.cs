using MicCheck;

// Stream Deck launches plugins with: -port <n> -pluginUUID <uuid> -registerEvent <event> -info <json>
// For development:  MicCheck.exe --console [seconds] [--test] [--always] [--list] [--device auto|default|<endpoint id>]
//                                                               prints what the key would show
//                   MicCheck.exe --svg <folder>                 writes sample key images for each state

var opts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
for (int i = 0; i + 1 < args.Length; i += 2)
    if (args[i].StartsWith('-')) opts[args[i].TrimStart('-')] = args[i + 1];

if (args.Length > 0 && args[0] == "--console") return RunConsole(args);
if (args.Length > 1 && args[0] == "--svg") return WriteSamples(args[1]);

if (!opts.TryGetValue("port", out string? port) || !opts.TryGetValue("pluginUUID", out string? uuid) || !opts.TryGetValue("registerEvent", out string? reg))
{
    Console.Error.WriteLine("This is a Stream Deck plugin. Use --console or --svg <folder> to try it outside Stream Deck.");
    return 1;
}

try
{
    await new StreamDeckPlugin().RunAsync(int.Parse(port), uuid, reg);
    return 0;
}
catch (Exception ex)
{
    Log.Write("Fatal: " + ex);
    return 2;
}

static int RunConsole(string[] args)
{
    int seconds = args.Length > 1 && int.TryParse(args[1], out int s) ? s : 5;
    bool test = args.Contains("--test");
    int devIndex = Array.IndexOf(args, "--device");
    var settings = new MeterSettings
    {
        Device = devIndex >= 0 && devIndex + 1 < args.Length ? args[devIndex + 1] : MeterSettings.Auto,
        AlwaysListen = args.Contains("--always"),
    };
    int exit = 0;
    var t = new Thread(() =>
    {
        try
        {
            var devices = new AudioDevices();
            devices.Refresh();
            if (args.Contains("--list"))
                foreach (CaptureDevice d in devices.All)
                    Console.WriteLine($"{(d.Id == devices.DefaultCommId ? "*" : " ")} {d.Id}  {d.Name}  apps=[{string.Join(", ", d.RecordingApps)}]");
            using var monitor = new MicMonitor(devices, settings);
            var frame = System.Diagnostics.Stopwatch.StartNew();
            var total = System.Diagnostics.Stopwatch.StartNew();
            bool pressed = !test;
            while (total.Elapsed.TotalSeconds < seconds)
            {
                monitor.Sample();
                if (frame.ElapsedMilliseconds >= 500)
                {
                    frame.Restart();
                    devices.Refresh();
                    // Press "the key" on the second tick
                    MicSnapshot snap = monitor.Tick(total.ElapsedMilliseconds > 900 && !pressed && (pressed = true));
                    Console.WriteLine($"{snap.Mode,-8} label={snap.Label,-10} device={snap.DeviceName}  muted={snap.Muted} vol={snap.VolumePercent}% level={snap.Level:0.00} noSignal={snap.NoSignal} testLeft={snap.TestSecondsLeft} other={snap.OtherDevice}");
                }
                Thread.Sleep(20);
            }
        }
        catch (Exception ex) { Console.WriteLine(ex); exit = 2; }
    });
    t.SetApartmentState(ApartmentState.MTA);
    t.Start();
    t.Join();
    return exit;
}

static int WriteSamples(string folder)
{
    Directory.CreateDirectory(folder);
    var rnd = new Random(4);
    float[] talking = Enumerable.Range(0, MicMonitor.HistoryLength).Select(i => i < 6 ? 0.08f + (float)rnd.NextDouble() * 0.05f : 0.45f + (float)rnd.NextDouble() * 0.45f).ToArray();
    float[] quiet = Enumerable.Range(0, MicMonitor.HistoryLength).Select(_ => 0.1f + (float)rnd.NextDouble() * 0.05f).ToArray();
    float[] zero = new float[MicMonitor.HistoryLength];
    const string dev = "Headset Mic";
    var samples = new Dictionary<string, MicSnapshot>
    {
        ["1-idle"] = new(MicMode.Idle, "IDLE", dev, false, 100, 0, zero, false, 0),
        ["2-teams-talking"] = new(MicMode.InUse, "Teams", dev, false, 100, talking[^1], talking, false, 0),
        ["3-teams-quiet"] = new(MicMode.InUse, "Teams", dev, false, 100, quiet[^1], quiet, false, 0),
        ["4-teams-nosignal"] = new(MicMode.InUse, "Teams", dev, false, 100, 0, zero, true, 0),
        ["5-muted"] = new(MicMode.InUse, "Teams", dev, true, 100, 0, zero, false, 0),
        ["6-test"] = new(MicMode.Test, "TEST", dev, false, 100, talking[^1], talking, false, 7),
        ["7-low-input"] = new(MicMode.InUse, "Discord", "USB Mic", false, 8, quiet[^1], quiet, false, 0),
        ["8-live"] = new(MicMode.Live, "LIVE", dev, false, 100, quiet[^1], quiet, false, 0),
        ["9-missing"] = new(MicMode.Missing, "NO MIC", "USB Mic", false, 100, 0, zero, false, 0),
        ["10-other-mic"] = new(MicMode.OtherMic, "Teams", dev, false, 100, 0, zero, false, 0, "Webcam Mic"),
        ["11-discord-talking"] = new(MicMode.InUse, "Discord", "USB Mic", false, 100, talking[^1], Shift(talking, 7), false, 0),
        ["12-zoom-talking"] = new(MicMode.InUse, "Zoom", "Webcam Mic", false, 100, talking[^1], Shift(talking, 13), false, 0),
        // Default image for the action, shown in the actions list and before the plugin draws the live meter
        ["key"] = new(MicMode.InUse, "Mic", "Mic Check", false, 100, talking[^1], talking, false, 0),
    };
    var html = new System.Text.StringBuilder("<html><body style=\"background:#333;color:#ddd;font-family:sans-serif\">");
    foreach (var (name, snap) in samples)
    {
        string svg = KeyRenderer.Render(snap);
        File.WriteAllText(Path.Combine(folder, name + ".svg"), svg);
        // Shown at 144 px and at 72 px, the real size of a Stream Deck MK.2 key
        html.Append($"<div style=\"display:inline-block;margin:8px;text-align:center;vertical-align:top\"><div style=\"border-radius:14px;overflow:hidden;width:144px;height:144px\">{svg}</div>"
            + $"<img src=\"{name}.svg\" width=\"72\" height=\"72\" style=\"border-radius:8px;margin-top:6px\"/><br/>{name}</div>");
    }
    File.WriteAllText(Path.Combine(folder, "index.html"), html.Append("</body></html>").ToString());
    Console.WriteLine("Wrote samples to " + folder);
    return 0;
}

static float[] Shift(float[] a, int by) => a.Skip(by).Concat(a.Take(by)).ToArray();
