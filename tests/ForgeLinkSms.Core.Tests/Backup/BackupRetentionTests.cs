using ForgeLinkSms.Core.Backup;

namespace ForgeLinkSms.Core.Tests.Backup;

public class BackupRetentionTests
{
    [Fact]
    public void Names_backups_by_local_date_and_time()
    {
        Assert.Equal("ForgeLink-backup-2026-11-02-0905.flbackup", BackupRetention.FileNameFor(new DateTime(2026, 11, 2, 9, 5, 0)));
    }

    [Fact]
    public void Keeps_the_newest_four_and_never_touches_other_files()
    {
        var names = new[]
        {
            "ForgeLink-backup-2026-10-01-0300.flbackup",
            "ForgeLink-backup-2026-10-08-0300.flbackup",
            "ForgeLink-backup-2026-10-15-0300.flbackup",
            "ForgeLink-backup-2026-10-22-0300.flbackup",
            "ForgeLink-backup-2026-10-29-0300.flbackup",
            "ForgeLink-backup-2026-11-05-0300.flbackup",
            "tax-return.pdf",
            "ForgeLink-backup-copy.flbackup",
            "ForgeLink-backup-2026-01-01-0000.flbackup.partial"
        };

        Assert.Equal(new[] { "ForgeLink-backup-2026-10-08-0300.flbackup", "ForgeLink-backup-2026-10-01-0300.flbackup" },
            BackupRetention.FilesToDelete(names));
    }

    [Fact]
    public void Four_or_fewer_backups_delete_nothing()
    {
        Assert.Empty(BackupRetention.FilesToDelete(new[] { "ForgeLink-backup-2026-10-01-0300.flbackup" }));
    }
}
