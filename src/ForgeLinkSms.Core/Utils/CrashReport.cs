using System.Globalization;

namespace ForgeLinkSms.Core.Utils;

public sealed record AppDetails(string Version, string Phone, string AndroidVersion);

// To is null for the developer address (CrashReport.To).
public sealed record ReportEmail(string Subject, string Body, string? To = null);

// Crash reports and feedback go out only as an email the person reviews and sends themselves.
public static class CrashReport
{
    public const string To = "Tmd1124@outlook.com";

    // Email apps silently drop or truncate very large mailto bodies.
    public const int MaxBodyLength = 6000;

    public static ReportEmail ForCrash(AppDetails app, DateTimeOffset when, string details)
    {
        var header = $"App: ForgeLink SMS {app.Version}\nPhone: {app.Phone}, {app.AndroidVersion}\n"
            + $"When: {when.ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture)}\n\n";
        var room = MaxBodyLength - header.Length;
        var trimmed = details.Length <= room ? details : details[..(room - "\n[cut]".Length)] + "\n[cut]";
        return new ReportEmail($"ForgeLink SMS crash report ({app.Version})", header + trimmed);
    }

    public static ReportEmail ForFeedback(AppDetails app) =>
        new($"ForgeLink SMS feedback ({app.Version})", $"\n\n\n---\nApp: ForgeLink SMS {app.Version}\nPhone: {app.Phone}, {app.AndroidVersion}");

    // The person's own chat with the assistant, so the developer can see what was asked and how it answered. The newest lines win when it is too long.
    public static ReportEmail ForAssistant(AppDetails app, string transcript)
    {
        const string Cut = "[earlier lines cut]\n";
        var header = $"\n\n\n---\nApp: ForgeLink SMS {app.Version}\nPhone: {app.Phone}, {app.AndroidVersion}\n\nAsk ForgeLink conversation:\n";
        var room = MaxBodyLength - header.Length;
        var text = transcript.Length <= room ? transcript : Cut + transcript[^(room - Cut.Length)..];
        return new ReportEmail($"ForgeLink SMS assistant report ({app.Version})", header + text);
    }
}
