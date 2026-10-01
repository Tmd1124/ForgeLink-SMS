using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Tests.Utils;

public class CrashReportTests
{
    private static readonly AppDetails App = new("1.0 (1)", "samsung SM-F966U1", "Android 16");
    private static readonly DateTimeOffset When = new(2026, 9, 30, 22, 30, 0, TimeSpan.FromHours(-4));

    [Fact]
    public void A_crash_email_has_a_clear_subject_and_the_app_phone_and_error_details()
    {
        var email = CrashReport.ForCrash(App, When, "NullReferenceException: boom\n   at Foo.Bar()");

        Assert.Equal("ForgeLink SMS crash report (1.0 (1))", email.Subject);
        Assert.Contains("App: ForgeLink SMS 1.0 (1)", email.Body);
        Assert.Contains("Phone: samsung SM-F966U1, Android 16", email.Body);
        Assert.Contains("When: 2026-09-30 22:30 -04:00", email.Body);
        Assert.EndsWith("NullReferenceException: boom\n   at Foo.Bar()", email.Body);
    }

    [Fact]
    public void Very_long_details_are_cut_to_a_safe_email_length_keeping_the_start()
    {
        var details = "Top: what failed\n" + new string('x', 50_000);

        var email = CrashReport.ForCrash(App, When, details);

        Assert.True(email.Body.Length <= CrashReport.MaxBodyLength);
        Assert.Contains("Top: what failed", email.Body);
        Assert.EndsWith("[cut]", email.Body);
    }

    [Fact]
    public void Feedback_email_leaves_room_to_write_above_the_phone_details()
    {
        var email = CrashReport.ForFeedback(App);

        Assert.Equal("ForgeLink SMS feedback (1.0 (1))", email.Subject);
        Assert.StartsWith("\n\n\n---\n", email.Body);
        Assert.Contains("Phone: samsung SM-F966U1, Android 16", email.Body);
    }

    [Fact]
    public void Reports_go_to_the_developer_address()
    {
        Assert.Equal("Tmd1124@outlook.com", CrashReport.To);
    }
}
