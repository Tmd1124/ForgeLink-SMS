using System.Globalization;

namespace ForgeLinkSms.Core.Utils;

public sealed record AppDetails(string Version, string Phone, string AndroidVersion);

public sealed record ReportEmail(string Subject, string Body);

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
}
