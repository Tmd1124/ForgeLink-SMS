using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Tests.Utils;

public class ErrorReportTests
{
    private static Exception Thrown(Exception ex)
    {
        try { throw ex; } catch (Exception caught) { return caught; }
    }

    [Fact]
    public void The_report_leads_with_the_error_type_and_message()
    {
        var report = ErrorReport.Format(Thrown(new InvalidOperationException("Sequence contains no elements")));

        Assert.StartsWith("InvalidOperationException: Sequence contains no elements", report);
    }

    [Fact]
    public void The_report_includes_the_inner_cause_and_where_it_happened()
    {
        var report = ErrorReport.Format(Thrown(new InvalidOperationException("outer", new FormatException("bad number"))));

        Assert.Contains("FormatException: bad number", report);
        Assert.Contains(nameof(ErrorReportTests), report);
    }
}
