using System.Globalization;
using System.Text.RegularExpressions;

namespace ForgeLinkSms.Core.Backup;

public static partial class BackupRetention
{
    public static string FileNameFor(DateTime local) =>
        $"ForgeLink-backup-{local.ToString("yyyy-MM-dd-HHmm", CultureInfo.InvariantCulture)}.flbackup";

    // Only files this app named are ever candidates; the date in the name sorts newest-first.
    public static IReadOnlyList<string> FilesToDelete(IEnumerable<string> names, int keep = 4) =>
        names.Where(n => BackupName().IsMatch(n))
            .OrderByDescending(n => n, StringComparer.Ordinal)
            .Skip(keep)
            .ToList();

    // Local time the newest backup in a folder was made, read from its name.
    public static DateTime? NewestBackupTime(IEnumerable<string> names) =>
        names.Where(n => BackupName().IsMatch(n))
            .Select(n => DateTime.TryParseExact(n["ForgeLink-backup-".Length..^".flbackup".Length], "yyyy-MM-dd-HHmm",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var when) ? when : (DateTime?)null)
            .Where(when => when is not null)
            .Max();

    [GeneratedRegex(@"^ForgeLink-backup-\d{4}-\d{2}-\d{2}-\d{4}\.flbackup$")]
    private static partial Regex BackupName();
}
