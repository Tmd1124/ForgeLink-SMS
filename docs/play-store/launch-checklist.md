# ForgeLink SMS — Play Console launch checklist

Work top to bottom. The exact text to paste for each form is in
[`play-console-answers.md`](play-console-answers.md); this list is the order and what to check.

## 0. Before you start

- [ ] Back up `C:\Users\Tmd11\ForgeLink-Keys\` (the upload key and its password file) somewhere safe
      and offline. Every future update must be signed with it.
- [ ] Build the bundle from `src/ForgeLinkSms`:
      `dotnet publish -c Release -f net10.0-android`
      → upload `bin/Release/net10.0-android/publish/com.forgelink.sms-Signed.aab`
      (version 1.0, version code 1, Android 7.0+). Check it's signed with the upload key:
      `keytool -printcert -jarfile <aab>` → SHA-256 starts `0F:90:9E:4B`.

## 1. Create the app

- [ ] Play Console → **Create app**: name `ForgeLink SMS`, default language English (US),
      App, Free (decide now — a free app can never become paid), accept the declarations.

## 2. Set up your app (Dashboard → "Set up your app")

- [ ] **Privacy policy** — host `docs/play-store/privacy-policy.html` on azureforgeai.com and paste
      its URL.
- [ ] **App access** — "All functionality available without special access", with the note that
      ForgeLink must be set as the default SMS app on first launch.
- [ ] **Ads** — No.
- [ ] **Content rating** — answers in *App content*.
- [ ] **Target audience** — 13+ (no under-13 groups).
- [ ] **Data safety** — answers in *Data safety* ("No data collected").
- [ ] **Government / financial / health / news** — No.
- [ ] **Sensitive permissions → SMS and Call Log** — *Default SMS handler*, paste the description,
      and add the screen-recording video link. Google reviews this by hand; expect days.
- [ ] **Foreground service permissions** — *Data sync* with the backup description and a short video
      of Settings → Backup → Back up now.

## 3. Store listing (Grow → Store presence → Main store listing)

- [ ] Name, short and full description (in *Store listing*), icon (512×512), feature graphic
      (1024×500), at least 2 phone screenshots (`images/Screenshots/` has three), category
      Communication, contact email.

## 4. Closed testing (Test and release → Testing → Closed testing)

New personal developer accounts must run a closed test with **at least 12 testers who stay opted
in for 14 days in a row** before production can be requested.

- [ ] Create a track (e.g. "Beta"), add a tester list (Google Groups or emails) with **12+ people**,
      choose countries.
- [ ] Create a release, opt in to **Play App Signing** when asked, upload the `.aab`, release notes:
      "First test build. Please use ForgeLink as your default SMS app for a few days and report
      anything odd by email to Tmd1124@outlook.com."
- [ ] Send review → once approved, share the opt-in link with testers.
- [ ] Testers: open the link, accept, install from Play, set ForgeLink as default SMS app,
      and use it day to day for 14 days (they must stay opted in).

## 5. Pre-launch report (Test and release → Testing → Pre-launch report → Settings)

- [ ] Make sure it's **on** (it runs automatically on closed and open test releases).
- [ ] Note: Google's test devices can't make ForgeLink the default SMS app, so their runs mostly
      stop at the setup screen. Use the report for **crashes, ANRs, accessibility and security
      warnings, and how the setup screen looks on other phones** — real testers cover the rest.
- [ ] After each upload, check the report (usually within an hour) and send me anything flagged.

## 6. Watch for problems while testing

- [ ] **Android vitals** (Quality → Android vitals) for crashes and "app not responding".
- [ ] Tester feedback.
- [ ] Each fix ships as a new build: raise `ApplicationVersion` (2, 3, …) and
      `ApplicationDisplayVersion` (1.1, …) in `ForgeLinkSms.csproj`, rebuild, upload a new release
      to the same track.

## 7. Production (after 14 days with 12+ testers)

- [ ] Dashboard → **Apply for production** and answer the questions about the test.
- [ ] Once granted: Production → Create release → promote the tested build.
