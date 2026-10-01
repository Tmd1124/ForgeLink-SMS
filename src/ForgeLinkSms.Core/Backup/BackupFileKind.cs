using System.Text;

namespace ForgeLinkSms.Core.Backup;

public enum BackupFileKind
{
    Unknown,
    FlBackup,
    EncryptedFlBackup,
    SmsBackupXml
}

public static class BackupFileKinds
{
    public static BackupFileKind Detect(ReadOnlySpan<byte> head)
    {
        if (BackupCrypto.IsEncrypted(head))
        {
            return BackupFileKind.EncryptedFlBackup;
        }
        if (head.Length >= 2 && head[0] == (byte)'P' && head[1] == (byte)'K')
        {
            return BackupFileKind.FlBackup;
        }
        var text = Encoding.UTF8.GetString(head).TrimStart('﻿', ' ', '\t', '\r', '\n');
        return text.StartsWith("<?xml", StringComparison.Ordinal) || text.StartsWith("<smses", StringComparison.Ordinal)
            ? BackupFileKind.SmsBackupXml
            : BackupFileKind.Unknown;
    }
}
