using System.Text;
using ForgeLinkSms.Core.Backup;

namespace ForgeLinkSms.Core.Tests.Backup;

public class BackupFileKindTests
{
    [Theory]
    [InlineData("FLBK\u0001", BackupFileKind.EncryptedFlBackup)]
    [InlineData("PK\u0003\u0004", BackupFileKind.FlBackup)]
    [InlineData("<?xml version='1.0' encoding='UTF-8' standalone='yes' ?>", BackupFileKind.SmsBackupXml)]
    [InlineData("﻿<?xml version=\"1.0\"?>", BackupFileKind.SmsBackupXml)]
    [InlineData("  \r\n<smses count=\"1\">", BackupFileKind.SmsBackupXml)]
    [InlineData("BEGIN:VCALENDAR", BackupFileKind.Unknown)]
    [InlineData("", BackupFileKind.Unknown)]
    public void Detects_the_kind_from_the_first_bytes(string head, BackupFileKind expected)
    {
        Assert.Equal(expected, BackupFileKinds.Detect(Encoding.UTF8.GetBytes(head)));
    }
}
