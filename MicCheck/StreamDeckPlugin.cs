using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace MicCheck;

/// <summary>
/// Talks to the Stream Deck app over its local WebSocket (SDK v2 protocol) and keeps every visible
/// Mic Check key updated with a freshly rendered meter image.
/// </summary>
internal sealed class StreamDeckPlugin
{
    public const string ActionUuid = "com.bp2008.miccheck.meter";
    private const int SampleMs = 20, FrameMs = 100;

    /// <summary>State for one visible key. Only touched on the audio thread.</summary>
    private sealed class Key
    {
        public required MicMonitor Monitor;
        public string? LastSvg;
        public bool Pressed;
    }

    private readonly ClientWebSocket ws = new();
    private readonly SemaphoreSlim sendLock = new(1, 1);
    /// <summary>Work for the audio thread, queued by the WebSocket receive loop (COM must stay on the audio thread).</summary>
    private readonly ConcurrentQueue<Action<Dictionary<string, Key>, AudioDevices>> commands = new();
    private volatile bool stopping;

    public async Task RunAsync(int port, string pluginUuid, string registerEvent)
    {
        await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}"), CancellationToken.None);
        await SendAsync(w => { w.WriteString("event", registerEvent); w.WriteString("uuid", pluginUuid); });
        Log.Write($"Registered with Stream Deck on port {port}");

        var audioThread = new Thread(AudioLoop) { IsBackground = true, Name = "MicCheck audio" };
        audioThread.SetApartmentState(ApartmentState.MTA);
        audioThread.Start();

        try { await ReceiveLoopAsync(); }
        finally { stopping = true; audioThread.Join(1000); }
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[64 * 1024];
        using var message = new MemoryStream();
        while (ws.State == WebSocketState.Open)
        {
            WebSocketReceiveResult r = await ws.ReceiveAsync(buffer, CancellationToken.None);
            if (r.MessageType == WebSocketMessageType.Close) break;
            message.Write(buffer, 0, r.Count);
            if (!r.EndOfMessage) continue;
            try { HandleMessage(message.ToArray()); }
            catch (Exception ex) { Log.Write("Bad message: " + ex.Message); }
            message.SetLength(0);
        }
        Log.Write("Stream Deck connection closed");
    }

    private void HandleMessage(byte[] json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        string? ev = root.TryGetProperty("event", out JsonElement e) ? e.GetString() : null;
        string? ctx = root.TryGetProperty("context", out JsonElement c) ? c.GetString() : null;
        string? action = root.TryGetProperty("action", out JsonElement a) ? a.GetString() : null;
        if (ctx == null || action != ActionUuid) return;

        JsonElement? payload = root.TryGetProperty("payload", out JsonElement p) ? p.Clone() : null;
        JsonElement? settingsJson = payload is { ValueKind: JsonValueKind.Object } pl && pl.TryGetProperty("settings", out JsonElement s) ? s : null;

        switch (ev)
        {
            case "willAppear":
            case "didReceiveSettings":
                MeterSettings settings = MeterSettings.From(settingsJson);
                commands.Enqueue((keys, devices) =>
                {
                    if (keys.TryGetValue(ctx, out Key? k)) k.Monitor.Settings = settings;
                    else keys[ctx] = new Key { Monitor = new MicMonitor(devices, settings) };
                    keys[ctx].LastSvg = null;
                });
                break;
            case "willDisappear":
                commands.Enqueue((keys, _) =>
                {
                    if (keys.Remove(ctx, out Key? k)) k.Monitor.Dispose();
                });
                break;
            case "keyDown":
                commands.Enqueue((keys, _) => { if (keys.TryGetValue(ctx, out Key? k)) k.Pressed = true; });
                break;
            case "propertyInspectorDidAppear":
            case "sendToPlugin":
                commands.Enqueue((_, devices) => SendDeviceList(ctx, devices));
                break;
        }
    }

    /// <summary>Gives the property inspector the current list of microphones for its drop-down.</summary>
    private void SendDeviceList(string ctx, AudioDevices devices)
    {
        var list = devices.All.Select(d => (id: d.Id, name: MicMonitor.ShortName(d.Name))).OrderBy(d => d.name, StringComparer.OrdinalIgnoreCase).ToArray();
        string defaultName = devices.DefaultComm is { } dc ? MicMonitor.ShortName(dc.Name) : "";
        SendAsync(w =>
        {
            w.WriteString("event", "sendToPropertyInspector");
            w.WriteString("action", ActionUuid);
            w.WriteString("context", ctx);
            w.WriteStartObject("payload");
            w.WriteString("type", "devices");
            w.WriteString("defaultComm", defaultName);
            w.WriteStartArray("devices");
            foreach (var (id, name) in list)
            {
                w.WriteStartObject();
                w.WriteString("id", id);
                w.WriteString("name", name);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }).GetAwaiter().GetResult();
    }

    private void AudioLoop()
    {
        var keys = new Dictionary<string, Key>();
        AudioDevices? devices = null;
        var frame = System.Diagnostics.Stopwatch.StartNew();
        while (!stopping)
        {
            try
            {
                devices ??= new AudioDevices();
                if (frame.ElapsedMilliseconds >= FrameMs || !commands.IsEmpty) devices.Refresh();
                while (commands.TryDequeue(out var command)) command(keys, devices);

                foreach (Key k in keys.Values) k.Monitor.Sample();
                if (frame.ElapsedMilliseconds >= FrameMs)
                {
                    frame.Restart();
                    foreach (var (ctx, k) in keys)
                    {
                        string svg = KeyRenderer.Render(k.Monitor.Tick(k.Pressed));
                        k.Pressed = false;
                        if (k.Monitor.TakeListenFailure())
                            SendAsync(w => { w.WriteString("event", "showAlert"); w.WriteString("context", ctx); }).GetAwaiter().GetResult();
                        if (svg == k.LastSvg) continue;
                        k.LastSvg = svg;
                        SendAsync(w =>
                        {
                            w.WriteString("event", "setImage");
                            w.WriteString("context", ctx);
                            w.WriteStartObject("payload");
                            w.WriteString("image", "data:image/svg+xml;charset=utf8," + svg);
                            w.WriteNumber("target", 0);
                            w.WriteEndObject();
                        }).GetAwaiter().GetResult();
                    }
                }
            }
            catch (Exception ex)
            {
                // Devices come and go (USB headsets, Bluetooth); start over with fresh COM objects
                Log.Write("Audio loop error: " + ex);
                foreach (Key k in keys.Values) k.Monitor.Dispose();
                devices = new AudioDevices();
                foreach (Key k in keys.Values) { k.Monitor = new MicMonitor(devices, k.Monitor.Settings); k.LastSvg = null; }
                Thread.Sleep(1000);
            }
            Thread.Sleep(SampleMs);
        }
        foreach (Key k in keys.Values) k.Monitor.Dispose();
    }

    /// <summary>Sends one JSON object; <paramref name="writeProperties"/> writes its properties.</summary>
    private async Task SendAsync(Action<Utf8JsonWriter> writeProperties)
    {
        var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            writeProperties(w);
            w.WriteEndObject();
        }
        byte[] bytes = buffer.ToArray();
        await sendLock.WaitAsync();
        try
        {
            if (ws.State == WebSocketState.Open)
                await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }
        finally { sendLock.Release(); }
    }
}

internal static class Log
{
    private static readonly object Gate = new();
    private static readonly string PathName = System.IO.Path.Combine(AppContext.BaseDirectory, "miccheck.log");

    public static void Write(string line)
    {
        lock (Gate)
        {
            try
            {
                var fi = new FileInfo(PathName);
                if (fi.Exists && fi.Length > 1_000_000) fi.Delete();
                File.AppendAllText(PathName, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {line}{Environment.NewLine}", Encoding.UTF8);
            }
            catch { }
        }
    }
}
