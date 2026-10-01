# Export and Import in SMS Backup & Restore Format — Design

**Date:** 2026-09-30
**Status:** Approved in conversation; awaiting written-spec review

## Goal

Let people move their texts between ForgeLink and other apps by writing and reading the XML format of SMS Backup & Restore (the most common Android SMS backup app), including picture messages and their media.

## Decisions (from the conversation)

- **Scope:** export and import, SMS and MMS with media.
- **Approach:** reuse the existing backup/restore pipeline (message source, duplicate index, restore target, WorkManager jobs, Check-first preview). Only the file format is new.
- **Content:** messages and media only. The format has no place for favorites, filters, schedules or settings, so none are written or read.
- **Encryption:** none (the other app expects plain XML).

## File format

UTF-8 XML as written by SMS Backup & Restore:

```xml
<?xml version='1.0' encoding='UTF-8' standalone='yes' ?>
<smses count="2" backup_set="<guid>" backup_date="<ms>" type="full">
  <sms protocol="0" address="+14045550199" date="<ms>" type="1" subject="null" body="Hi" toa="null" sc_toa="null"
       service_center="null" read="1" status="-1" locked="0" date_sent="<ms>" sub_id="-1"
       readable_date="Sep 30, 2026 4:12:00 PM" contact_name="Kim" />
  <mms date="<ms>" rr="null" sub="null" ct_t="application/vnd.wap.multipart.related" read_status="null" seen="1"
       msg_box="1" address="+14045550199~+17705550101" sub_cs="null" resp_st="null" retr_st="null" d_tm="null"
       text_only="0" exp="null" locked="0" m_id="null" st="null" retr_txt_cs="null" retr_txt="null" creator="null"
       date_sent="<s>" read="1" m_size="null" rpt_a="null" ct_cls="null" pri="null" sub_id="-1" tr_id="null"
       resp_txt="null" ct_l="null" m_cls="null" d_rpt="null" v="18" _id="<n>" m_type="132"
       readable_date="…" contact_name="Kim, Sam">
    <parts>
      <part seq="0" ct="text/plain" name="null" chset="106" cd="null" fn="null" cid="&lt;text0&gt;" cl="text0.txt"
            ctt_s="null" ctt_t="null" text="Look" />
      <part seq="1" ct="image/jpeg" name="IMG_7.jpg" chset="null" cd="null" fn="null" cid="&lt;image1&gt;"
            cl="IMG_7.jpg" ctt_s="null" ctt_t="null" text="null" data="<base64>" />
    </parts>
    <addrs>
      <addr address="+17705550101" type="137" charset="106" />
      <addr address="+14045550199" type="151" charset="106" />
    </addrs>
  </mms>
</smses>
```

- `sms.type`: 1 = received, 2 = sent. `mms.msg_box`: 1 = received, 2 = sent. `m_type` 132 = received, 128 = sent.
- `sms.date` and `date_sent` are milliseconds; `mms.date` is milliseconds in the file, `mms.date_sent` is seconds (as the app writes them).
- `mms.address` is all participants joined by `~`. `addr type="137"` is the sender, `151` a recipient.
- Media parts carry base64 `data`; text parts carry `text`. The literal string `null` means "no value".
- Files are named `sms-YYYYMMDDHHmmss.xml`, which SMS Backup & Restore lists and restores.

## Mapping to ForgeLink's backup model

- **Export:** `IBackupSource` already yields `SourceMessage` (a `BackupMessage` plus attachment streams). A new `SmsBackupXmlWriter` writes each one as `<sms>` or `<mms>`, streaming each attachment through base64 without holding a whole backup in memory. `count` is written from `CountMessages()` up front.
- **Import:** a new `SmsBackupXmlReader` reads the file forward-only (`XmlReader`) and yields `BackupMessage` records. For each MMS media part it decodes the base64 to a temp file in the work directory and hands the restore target a stream to that file. Temp files are deleted when the import ends.
- The existing `RestoreRunner` (duplicate index, inserts, preview) runs on those messages. It already takes a message sequence and a media opener, so it gets a small abstraction for "a readable backup": `IRestoreSource { Manifest counts; ReadMessages(); OpenMedia(); AppData (empty for XML) }`, implemented by both `BackupReader` (`.flbackup`) and `SmsBackupXmlReader`.

## Behaviour

- **Settings → Backup → "Export for other apps (SMS Backup & Restore)":** the Save-as picker, suggested name `sms-YYYYMMDDHHmmss.xml`, then a background job with a progress notification and Cancel. Status line: "Exported N messages and M photos/videos for other apps."
- **Import:** "Restore from a backup" accepts both `.flbackup` and SMS Backup & Restore `.xml`, told apart by the first bytes (`FLBK`, zip `PK`, or `<?xml`). For XML, the password field and "Also restore my settings / scheduled texts" are hidden. **Check backup** shows the date (from `backup_date`), counts, and how many would be added. Then Restore.
- **Duplicates:** the same rule as Restore: same participants, direction, time within 1 second and same text (and attachment count for MMS). Importing twice adds nothing.
- **Requires** ForgeLink to be the default SMS app, like Restore.

## Errors

- Not an SMS Backup & Restore file (wrong root, not XML): "This isn't an SMS Backup & Restore file." Nothing is imported.
- Malformed or truncated XML or bad base64: "This file is damaged or incomplete." Messages already inserted before the problem stay (like an interrupted restore; running it again skips them).
- A single very large media part is decoded in one piece (XML attributes can't be streamed); parts over 100 MB are skipped and counted in the result ("1 attachment was too large to import").

## Components

**Core (unit-tested):**
- `Backup/SmsBackupXmlWriter.cs`: writes the format from `SourceMessage`s.
- `Backup/SmsBackupXmlReader.cs`: parses the format into `BackupMessage`s and media temp files.
- `Backup/IRestoreSource.cs`: the common read interface; `BackupReader` implements it too.
- `Backup/BackupFileKind.cs`: tells `.flbackup` (plain or encrypted) from XML by the first bytes.
- `RestoreRunner`: takes `IRestoreSource` instead of `BackupReader`.

**Android:**
- `ExportWorker` (WorkManager, foreground dataSync), like `BackupWorker`.
- `RestoreWorker` / `BackupService.PreviewRestoreAsync`: open XML files with `SmsBackupXmlReader`.
- `SettingsPage`: the Export button, and the XML-aware restore sheet.

## Testing

- **Round trip:** export fake SMS, group MMS (text + photo parts) and emoji/`<&">` text, re-read, same `BackupMessage`s and media bytes.
- **Real-world sample:** a hand-written file in the app's exact layout (the `null` strings, `~` addresses, 137/151 addrs, seconds vs ms) reads correctly.
- **Errors:** wrong root, truncated file, bad base64 → the errors above; an oversized part is skipped and counted.
- **Runner:** preview and restore from an XML source add missing messages and skip existing ones.
- **On the phone:** export to Downloads, check the file opens as XML with the expected counts, then "Check backup" on it shows 0 to add; no real messages are deleted.

## Out of scope

- Call logs (SMS Backup & Restore can also back up calls).
- Importing favorites, filters or settings from other apps.
- Encrypted or zipped SMS Backup & Restore archives.
