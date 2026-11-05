# App updates (until Google Play)

ForgeLink checks `https://cloudcompassai.com/downloads/forgelink-sms/latest.json` (at most every 12 hours, plus whenever you tap Check for updates). Builds installed from Google Play skip all of this.

## Publishing a release

1. Raise `ApplicationVersion` (integer, must be higher than the last release) and `ApplicationDisplayVersion` in `ForgeLinkSms.csproj`.
2. Build a **release APK signed with the same key as the installed app**. Android refuses an update signed with a different key, and a debug build can't update a release build.
3. Upload the APK to `https://cloudcompassai.com/downloads/forgelink-sms/` (the APK URL must be https on cloudcompassai.com).
4. Get its hash: `Get-FileHash forgelink-sms-1.3.apk -Algorithm SHA256`
5. Upload `latest.json` last, so phones never see a manifest pointing at a missing file:

```json
{
  "versionCode": 4,
  "versionName": "1.3",
  "apkUrl": "https://cloudcompassai.com/downloads/forgelink-sms/forgelink-sms-1.3.apk",
  "sha256": "<64 hex characters>",
  "notes": "What changed, one line per change."
}
```

The app discards a download whose SHA-256 doesn't match, and ignores a manifest whose `apkUrl` isn't https on cloudcompassai.com.

## When it moves to Google Play

Play-installed copies already hide the update UI. Remove the Check for updates row and `UpdateService` once the sideload build is retired.
