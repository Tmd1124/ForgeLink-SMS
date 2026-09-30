# Backup and Restore — Design

**Date:** 2026-09-30
**Status:** Approved in conversation; awaiting written-spec review

## Goal

Let people back up everything ForgeLink holds — all texts, picture messages with their media, and ForgeLink's own data and settings — to a file they choose, manually or weekly, optionally protected by a password, and restore it onto the same or a new phone without ever deleting or duplicating anything.

## Decisions (from the conversation)

- **Contents:** everything — SMS, MMS with media, and ForgeLink data and settings.
- **When:** a "Back up now" button, plus an optional weekly automatic backup to a folder the user picks once. Weekly backups keep the newest 4.
- **Password:** optional. When set, the file is encrypted; a forgotten password means the backup can't be opened, and the app says so clearly.
- **Format:** ForgeLink's own `.flbackup` file. An "Export for other apps" (SMS Backup & Restore XML) is out of scope for now.

## Backup file (`.flbackup`)

A zip archive (the whole zip is encrypted when a password is set):

| Entry | Contents |
|---|---|
| `manifest.json` | `format` = 1, `app_version`, `created_utc`, counts (`sms`, `mms`, `media_files`, `threads`), `encrypted` (always false inside; the outer wrapper records encryption) |
| `messages.jsonl` | One JSON object per message: `kind` (sms/mms), `addresses` (all participants for groups), `timestamp_ms`, `date_sent_ms`, `outgoing`, `read`, `status`, `body`, `subject`, `attachments` (list of `{ media: "media/<n>.<ext>", content_type, file_name }`) |
| `media/` | Attachment files, streamed in and out, never held whole in memory |
| `forgelink.json` | Favorites, filters (name, color, emoji) and their members, quick replies, scheduled messages, archived/trashed/snoozed/muted conversations, blocked numbers, allowed senders, drafts, and settings (display, notifications, theme, text size, link previews). Conversations are identified by their participant addresses, not Android thread ids, because thread ids differ on another phone. "Forwarded to…" markers are not backed up: they are keyed by message ids that don't exist on another phone. |

**Encrypted file layout:** `FLBK` magic (4 bytes) · version byte (1) · salt (16 bytes) · PBKDF2 iterations (4 bytes, 600,000) · then the zip encrypted in 1 MiB chunks with AES-256-GCM, where each chunk stores its 12-byte nonce and 16-byte tag, and the last chunk is flagged so truncation is detected. The key comes from PBKDF2-HMAC-SHA256(password, salt, iterations). An unencrypted backup is the plain zip.

Files are named `ForgeLink-backup-YYYY-MM-DD-HHmm.flbackup`.

## Behaviour

**Back up now** — Settings → Backup → "Back up now": save to the weekly folder if one is set, otherwise the system "Save as" screen. Runs as a background job with a progress notification ("12,400 messages · 830 photos") and a Cancel action. Shows the last backup's date, size and result.

**Weekly automatic** — optional switch; the user picks a folder once (Google Drive, Downloads, etc.). Runs about every 7 days while charging and not low on battery. After a successful backup, deletes older files matching `ForgeLink-backup-*.flbackup` in that folder so the newest 4 remain — never any other file. A failure raises a notification with the reason.

**Restore** — Settings → Backup → "Restore from a backup": pick a file → enter the password if needed → "Check backup" reads the whole file without writing anything and shows its date, message and media counts, and exactly how many messages a restore would add → optional "Also restore my settings" (off by default) → confirm. Runs as a background job with progress.
- Only adds; never deletes or overwrites.
- A backed-up message is skipped when a message already exists with the same participants, direction, time within 1 second, and the same body (for MMS: the same body and attachment count).
- ForgeLink data merges: filters with the same name (case-insensitive) are combined and gain members; favorites, blocks, allowed senders and quick replies (same text) are added only if missing. Archive/trash/snooze/mute states and drafts are applied only to conversations that gained messages in this run.
- Scheduled texts that are still in the future are re-added only when the user ticks "Also re-add N scheduled texts" (shown after Check backup when N > 0), because they will really be sent; duplicates (same recipients, time and text) are skipped.
- Settings are replaced only when the user ticks the box.
- Requires ForgeLink to be the default SMS app (it is; if not, Restore explains and stops).
- Checks free space before reading the backup, and clears leftovers from an interrupted restore first.
- Restoring again is harmless: everything already present is skipped.

## Components

**Core (`ForgeLinkSms.Core`, unit-tested):**
- `Backup/BackupModels.cs` — manifest, message and app-data records.
- `Backup/BackupWriter` / `BackupReader` — write and read the zip entries and JSONL as streams.
- `Backup/BackupCrypto` — chunked AES-256-GCM with PBKDF2; distinguishes "wrong password" from "damaged file".
- `Backup/DuplicateMatcher` — the "already on the phone" rule.
- `Backup/AppDataMerger` — combines ForgeLink data by the rules above.
- `Backup/BackupRetention` — which old weekly backups to delete.
- `Backup/IBackupSource` / `IRestoreTarget` interfaces so the orchestration (`BackupRunner`, `RestoreRunner`) is testable with fakes.

**Android:**
- `BackupSource` — reads SMS, MMS and media (reusing `MmsReader`), and ForgeLink repositories and settings.
- `RestoreTarget` — inserts SMS (inbox/sent with read state and times) and MMS (pdu row, address rows, parts with media) into the provider; writes ForgeLink data.
- `BackupWorker`, `RestoreWorker` (WorkManager, `Xamarin.AndroidX.Work.Runtime`) — foreground work with a progress notification and cancel; periodic 7-day request with charging and battery-not-low constraints.
- Folder access through the Storage Access Framework: a persisted tree permission for the weekly folder, and `ACTION_CREATE_DOCUMENT` for one-off saves; `ACTION_OPEN_DOCUMENT` for restore.
- Settings → Backup section in `SettingsPage`.

## Errors

- Out of space or folder unavailable: stop, delete the partial file, notify with the reason; if the weekly folder's permission is gone, the schedule pauses and Settings asks for a new folder.
- Wrong password: "That password doesn't open this backup." Damaged or truncated file: "This backup file is damaged or incomplete." Newer format than the app understands: "This backup was made by a newer version of ForgeLink — update the app first." Nothing is restored in any of these cases.
- A restore interrupted halfway can be run again; duplicates are skipped.
- Media is streamed to avoid the large-thread memory failures seen earlier.

## Testing

**Unit (Core):** round trip without and with a password; wrong password; truncated and tampered files; newer-format rejection; duplicate matching (SMS, MMS, group, 1-second tolerance); app-data merge (filters by name, no duplicate favorites/blocks/quick replies/scheduled messages, settings only when asked); retention picks only matching file names and keeps the newest 4; runner behaviour with fake source/target (progress, cancel, skip counts).

**On the phone:** back up to Downloads, then restore the same file onto the same phone and confirm nothing duplicates; restore is only ever run with the user's own backup, and no real messages are deleted for testing.

## Out of scope

- SMS Backup & Restore XML export/import.
- Automatic cloud upload beyond the folder the user picks (e.g. a Drive API integration).
- Restoring onto a phone where ForgeLink isn't the default SMS app.
