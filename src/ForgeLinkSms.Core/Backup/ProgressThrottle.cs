namespace ForgeLinkSms.Core.Backup;

// Posting a notification for every 100 messages floods the system on a big backup; once a
// second is plenty for a progress bar. The last update always goes through so it reads "done".
public sealed class ProgressThrottle(TimeSpan interval)
{
    private DateTime? _last;

    public bool ShouldReport(DateTime now, int done, int total)
    {
        if (_last is { } last && now - last < interval && done < total)
        {
            return false;
        }
        _last = now;
        return true;
    }
}
