using ForgeLinkSms.Core.Backup;

namespace ForgeLinkSms.Core.Tests.Backup;

public class ProgressThrottleTests
{
    private static readonly DateTime T0 = new(2026, 10, 1, 12, 0, 0);

    [Fact]
    public void Reports_the_first_update_then_at_most_once_a_second()
    {
        var throttle = new ProgressThrottle(TimeSpan.FromSeconds(1));

        Assert.True(throttle.ShouldReport(T0, done: 100, total: 10_000));
        Assert.False(throttle.ShouldReport(T0.AddMilliseconds(400), done: 200, total: 10_000));
        Assert.False(throttle.ShouldReport(T0.AddMilliseconds(999), done: 300, total: 10_000));
        Assert.True(throttle.ShouldReport(T0.AddMilliseconds(1_000), done: 400, total: 10_000));
    }

    [Fact]
    public void Always_reports_the_final_update()
    {
        var throttle = new ProgressThrottle(TimeSpan.FromSeconds(1));
        throttle.ShouldReport(T0, done: 9_900, total: 10_000);

        Assert.True(throttle.ShouldReport(T0.AddMilliseconds(10), done: 10_000, total: 10_000));
    }
}
