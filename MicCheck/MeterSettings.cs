using System.Text.Json;

namespace MicCheck;

/// <summary>Per-key settings, stored by Stream Deck and edited in the property inspector (plugin\pi\index.html).</summary>
internal sealed record MeterSettings
{
    public const string Auto = "auto", DefaultComm = "default";

    /// <summary><see cref="Auto"/>, <see cref="DefaultComm"/>, or a capture endpoint ID.</summary>
    public string Device { get; init; } = Auto;

    /// <summary>Friendly name of the chosen endpoint, so the key can name it while it is unplugged.</summary>
    public string DeviceName { get; init; } = "";

    /// <summary>How long a key press records for when nothing else is using the mic.</summary>
    public int TestSeconds { get; init; } = 10;

    /// <summary>Keep recording all the time so the meter is always live (Windows will show the mic as in use).</summary>
    public bool AlwaysListen { get; init; }

    public static MeterSettings From(JsonElement? settings)
    {
        var s = new MeterSettings();
        if (settings is not { ValueKind: JsonValueKind.Object } o) return s;
        if (o.TryGetProperty("device", out JsonElement d) && d.ValueKind == JsonValueKind.String && d.GetString() is { Length: > 0 } dev) s = s with { Device = dev };
        if (o.TryGetProperty("deviceName", out JsonElement n) && n.ValueKind == JsonValueKind.String) s = s with { DeviceName = n.GetString() ?? "" };
        if (o.TryGetProperty("testSeconds", out JsonElement t) && int.TryParse(t.ToString(), out int secs)) s = s with { TestSeconds = Math.Clamp(secs, 1, 600) };
        if (o.TryGetProperty("alwaysListen", out JsonElement a)) s = s with { AlwaysListen = a.ValueKind == JsonValueKind.True || (a.ValueKind == JsonValueKind.String && a.GetString() == "true") };
        return s;
    }
}
