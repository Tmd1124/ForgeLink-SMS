namespace ForgeLinkSms.Core.Backup;

// Messages are shown to the user as-is.
public class BackupException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class BackupPasswordException(bool required)
    : BackupException(required ? "This backup is protected. Enter its password." : "That password doesn't open this backup.")
{
    public bool Required { get; } = required;
}

public sealed class BackupDamagedException(Exception? inner = null)
    : BackupException("This backup file is damaged or incomplete.", inner);

public sealed class BackupTooNewException()
    : BackupException("This backup was made by a newer version of ForgeLink — update the app first.");
