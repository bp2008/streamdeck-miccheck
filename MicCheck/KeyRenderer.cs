using System.Globalization;
using System.Text;

namespace MicCheck;

/// <summary>Draws a 144×144 SVG key image for a <see cref="MicSnapshot"/>.</summary>
internal static class KeyRenderer
{
    private const int Size = 144;
    private const int MeterTop = 36, MeterBottom = 104;

    private const string Green = "#22c55e", Yellow = "#eab308", Red = "#ef4444", Grey = "#6b7280", DimGrey = "#374151", White = "#ffffff";

    public static string Render(MicSnapshot s)
    {
        var sb = new StringBuilder();
        string bg = s.Muted && s.Mode != MicMode.Missing ? "#7f1d1d" : "#000000";
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{Size}\" height=\"{Size}\" viewBox=\"0 0 {Size} {Size}\">");
        sb.Append($"<rect width=\"{Size}\" height=\"{Size}\" fill=\"{bg}\"/>");

        // Top label: which app is using the mic (or IDLE / TEST / LIVE / MUTED / NO MIC)
        bool showMuted = s.Muted && s.Mode != MicMode.Missing;
        string label = showMuted ? "MUTED" : s.Label;
        string labelColor = showMuted ? White : s.Mode switch
        {
            MicMode.Idle => Grey,
            MicMode.Test or MicMode.OtherMic => Yellow,
            MicMode.Live => "#d1d5db",
            MicMode.Missing => Red,
            _ => White,
        };
        if (s.Mode == MicMode.Test && s.TestSecondsLeft > 0 && !showMuted) label = $"TEST {s.TestSecondsLeft}s";
        // Text sizes are for a 144 px image; a Stream Deck MK.2 key shows it at 72 px, so everything is drawn large.
        Text(sb, 72, 28, 26, labelColor, Fit(label, 9), bold: true);

        if (s.Mode == MicMode.Missing)
        {
            Text(sb, 72, 66, 20, Grey, "Not", bold: true);
            Text(sb, 72, 88, 20, Grey, "connected", bold: true);
        }
        else if (s.Mode == MicMode.OtherMic)
        {
            // The call app is using a different mic: name that mic, since it is the one people are hearing
            Text(sb, 72, 66, 20, Yellow, "is using", bold: true);
            Text(sb, 72, 88, 20, Yellow, ShortDevice(s.OtherDevice), bold: true);
            Text(sb, 72, 130, 20, Grey, ShortDevice(s.DeviceName), bold: true);
            sb.Append("</svg>");
            return sb.ToString();
        }
        else if (showMuted)
        {
            Text(sb, 72, 80, 20, White, "in Windows", bold: true);
        }
        else if (s.Mode == MicMode.Idle)
        {
            // Flat baseline plus a hint
            sb.Append($"<rect x=\"6\" y=\"{MeterBottom - 2}\" width=\"132\" height=\"2\" fill=\"{DimGrey}\"/>");
            Text(sb, 72, 66, 20, Grey, "Press", bold: true);
            Text(sb, 72, 88, 20, Grey, "to test", bold: true);
        }
        else
        {
            DrawHistory(sb, s.History);
            if (s.NoSignal)
            {
                sb.Append($"<rect x=\"6\" y=\"50\" width=\"132\" height=\"38\" rx=\"6\" fill=\"#000000\" fill-opacity=\"0.8\"/>");
                Text(sb, 72, 77, 22, Red, "NO SIGNAL", bold: true);
            }
        }

        // Low Windows input level is a common silent failure, so it takes over the footer
        string footColor = s.Mode is MicMode.Idle or MicMode.Missing && !showMuted ? Grey : "#d1d5db";
        if (s.VolumePercent < 20 && !s.Muted && s.Mode != MicMode.Missing)
            Text(sb, 72, 130, 20, Yellow, $"Input {s.VolumePercent}%", bold: true);
        else
            Text(sb, 72, 130, 20, footColor, ShortDevice(s.DeviceName), bold: true);

        sb.Append("</svg>");
        return sb.ToString();
    }

    /// <summary>Scrolling bar graph of recent levels; newest on the right.</summary>
    private static void DrawHistory(StringBuilder sb, float[] history)
    {
        const double left = 6, width = 132, gap = 1.5;
        double barW = (width - gap * (history.Length - 1)) / history.Length;
        int h = MeterBottom - MeterTop;
        // Guide lines at -20 dB (good speaking level starts around here) and -6 dB (getting hot)
        float yellowAt = MicMonitor.ToMeterScale(-20), redAt = MicMonitor.ToMeterScale(-6);
        foreach (double mark in new[] { yellowAt, redAt })
            sb.Append($"<rect x=\"{F(left)}\" y=\"{F(MeterBottom - mark * h)}\" width=\"{F(width)}\" height=\"1\" fill=\"{DimGrey}\"/>");
        for (int i = 0; i < history.Length; i++)
        {
            double v = Math.Max(history[i], 0.03);
            double bh = v * h;
            string color = history[i] >= redAt ? Red : history[i] >= yellowAt ? Yellow : history[i] > 0 ? Green : DimGrey;
            double x = left + i * (barW + gap);
            sb.Append($"<rect x=\"{F(x)}\" y=\"{F(MeterBottom - bh)}\" width=\"{F(barW)}\" height=\"{F(bh)}\" rx=\"1\" fill=\"{color}\"/>");
        }
    }

    private static void Text(StringBuilder sb, double x, double y, int size, string color, string text, bool bold)
    {
        if (text.Length == 0) return;
        sb.Append($"<text x=\"{F(x)}\" y=\"{F(y)}\" font-family=\"Segoe UI, Arial, sans-serif\" font-size=\"{size}\" font-weight=\"{(bold ? "700" : "400")}\" fill=\"{color}\" text-anchor=\"middle\">{Escape(text)}</text>");
    }

    /// <summary>
    /// Shortens a device name to whole words that fit on one line of the key:
    /// "BlackShark V3 Pro - Chat" → "BlackShark V3", "Logitech G933 Gaming Headset" → "Logitech G933".
    /// </summary>
    private static string ShortDevice(string name)
    {
        const int max = 13;
        int dash = name.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0) name = name[..dash];
        if (name.Length <= max) return name;
        string result = "";
        foreach (string word in name.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string next = result.Length == 0 ? word : result + " " + word;
            if (next.Length > max) break;
            result = next;
        }
        return result.Length > 0 ? result : Fit(name, max);
    }

    private static string Fit(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
    private static string F(double d) => d.ToString("0.##", CultureInfo.InvariantCulture);
    private static string Escape(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
