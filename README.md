# ForgeLink SMS

## Pick up here

*Last updated 2026-11-06.*

**Run it:** connect a physical Android device over USB (the emulator does not run on this ARM64 machine). `adb` is not on PATH; it lives at `C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe`. Run `adb devices` (the phone must show `device`, not `unauthorized`), then `dotnet build src/ForgeLinkSms/ForgeLinkSms.csproj -t:Run -f net10.0-android` with that folder on PATH. The app targets `net10.0-android` (`net9.0-android` fails with NETSDK1005). Unit tests: `dotnet test tests/ForgeLinkSms.Core.Tests` (883 pass). The phone is a foldable with two displays, so a bare `adb shell screencap` returns the black cover screen or a stale file; list ids with `adb shell dumpsys SurfaceFlinger --display-id` and capture with `screencap -d <id>` (the second id is the main screen).

**Deploy:** CI is `azure-pipelines.yml`. Push `master` to both remotes (`origin` = GitHub, `azuredevops`).

**State of play:**
- 2026-11-06: **Latest release 2.2 (versionCode 13), confirmed working on Travis's Fold7.** Since 1.5: 1.6 floating Ask ForgeLink button; 1.7 maintenance (exercised the fixed release script); 1.8 Select all in the chat-list selection bar; 1.9 `SmsService.SendAsync` now runs off the UI thread (it did a blocking SMS-store insert on the UI thread and caused the "isn't responding" ANR on Send); 2.0 Googling (web search) in Ask ForgeLink; 2.1 press-and-hold to drag the floating button (`wwwroot/js/draggableFab.js`, position kept in localStorage); 2.2 Googling on by default and remembered (Preferences `assistant_googling`), only for questions `HelpMatcher.IsAboutApp` says are not about ForgeLink (best help-topic score under 3). How Googling works: app -> `cloudcompassai.com/api/assistant.php` (`web:true`) -> Tavily search (free plan, 1,000 credits a month; server cap `web_monthly_cap` 800) -> Gemini answers from those results only -> answer plus source links. Google's own Search grounding is NOT available on the free Gemini key (HTTP 429 quota), which is why Tavily is used. Keys live only in the server's gitignored `api/assistant_config.php` (`gemini_api_key`, `tavily_api_key`). Decided by Travis 2026-11-05/06: free of charge for him and users, no billing on the Google project, Googling default on.
- Release routine: bump `ApplicationDisplayVersion`/`ApplicationVersion` in the csproj, commit and push both remotes, then `scriptselease.ps1 -Notes "..." -Publish` (it now ignores the FTP host's harmless `451` retry messages; a real failure leaves the website repo's `publish-app-release.sh` restoring its local manifest so a retry works). The website's `api/` files and privacy page deploy separately with `scripts/deploy-hostgator.sh --target cloudcompassai <files>` from the Cloud Compass Website repo (push that repo to its `github` remote; `old-github` is stale). Another Claude session also edits both repos, so stage files explicitly and check `git status` before releasing.
- Open: the in-app update path from a release build to the next was confirmed by Travis's phone. Test devices: debug builds can't update release builds in place (different signing key), so back up, uninstall, install the release, restore. The on-device assistant (Gemini Nano, call by reflection in `OnDeviceAiService`) is untested beyond Travis's Fold7. Web search quality on borderline questions is only lightly tested.
- 2026-11-05: **1.4 and 1.5 released** with `scripts\release.ps1 -Notes "..." -Publish` (builds, signs with the upload key, uploads APK then `latest.json` to cloudcompassai.com/downloads/forgelink-sms/; see `docs/app-updates.md`). New in 1.4: on-device assistant (Gemini Nano via ML Kit GenAI, called by reflection from `Platforms/Android/OnDeviceAiService.cs` because the Kotlin API can't be bound; jars in `Platforms/Android/libs`, bindings stripped in `Transforms/Metadata.xml`, R8 keep rules in `proguard.cfg`), thread-header sparkles button (Suggest a reply runs by default, Summarize), About page (Menu > App > About), optional update check (Settings > Check for Updates, off by default, cold start only). 1.5: assistant answers cleaned of markdown/lead-in (`AssistantPrompts.Clean`) and the Ask ForgeLink help chat. `Transforms/Metadata.xml` must stay registered as a `<TransformFile>` in the csproj; without it, builds randomly fail with `Com.Google.Mlkit.Genai.*` binding errors (fix then: delete `obj/Debug/net10.0-android/generated` and `api.xml.*`). The phone's debug build can't be updated in place by a release APK (different signing key): back up, uninstall, install the release, restore from the backup.
- 2026-11-05: **Ask ForgeLink help assistant** built (spec/plan `docs/superpowers/{specs,plans}/2026-11-05-help-assistant*`). Menu > Ask ForgeLink (`Pages/Assistant/AssistantPage.razor`, route `/assistant`). Core: `HelpMatcher` (keyword-picks help topics), `AssistantFixedAnswers` (price/refund/privacy are fixed text, never sent), `AssistantClient` + `AssistantService` (cloud answer from `https://cloudcompassai.com/api/assistant.php`, else offline help text). Unit tests pass. NOT live: the server endpoint answers 503 until Travis puts a Gemini API key (Google AI Studio, project with NO billing) in the server's `api/assistant_config.php`; until then the chat shows offline help text. Not yet tried on the phone. Privacy policy updated (Nov 5). Needs a version bump and `scripts\release.ps1` to ship.
- 2026-11-05: **In-app rating** built (spec/plan in `docs/superpowers/{specs,plans}/2026-11-05-in-app-rating*`). `/rate` now POSTs stars + optional message + optional name to `https://cloudcompassai.com/api/app_review_submit.php` (`RatingClient` in Core, `RatingService` in `Platforms/Android`; random install id in Preferences); failures keep the text and offer "Send by email instead". Non-Play installs get a slim "Enjoying ForgeLink?" card on Chats after a send once 7 days / 20 sent (`ReviewPromptRules.ShouldShowCard`, "Not now" = 120 days); Menu has a "Rate ForgeLink" row. Server side is LIVE on cloudcompassai (see that repo's CLAUDE.md). 883 unit tests pass. NOT yet checked on the phone (it was locked): open Menu > Rate ForgeLink, send a test, then reject it in admin Reviews. Version is now 1.2 / versionCode 3. Customer APK: `src/ForgeLinkSms/bin/Release/net10.0-android/ForgeLink-SMS-1.2-v3.apk`, signed with the upload key (cert SHA-256 `0f909e4b...a62cee`); upload it in cloudcompassai `admin/products.php` ("APK version" 1.2). The MSBuild signing step fails for `.apk` (same one-line password file used for store and key password), so build with `-c Release -f net10.0-android -p:AndroidPackageFormat=apk -p:AndroidKeyStore=false`, then run `zipalign -f -p 16` and `apksigner sign --ks ForgeLink-Keys\forgelink-upload.jks --ks-pass file:...upload-key-password.txt --ks-key-alias forgelink-upload --min-sdk-version 24` by hand on `obj\Release\net10.0-android\android\bin\com.forgelink.sms.apk`.
- Previous code commit `b2429a3`, pushed to both remotes. Fixes the "Something went wrong" crash on opening the app: a thread whose newest message had no address sent an empty number to the contact lookup. `ThreadService` now falls back to the thread's single recipient, and `ContactService` ignores blank numbers. Installed on the phone and checked there on 2026-11-05: the conversations list loads, Tiffani Donnelly's thread is back in favorites, opens with her name, and tapping her name in the thread header (the contact button; the (i) is a help sheet) opens her contact card without an error. The two Android-only files have no unit tests.
- Earlier code commit `4a683c8`. It hides the Unread/Updates/Screener buttons when they are empty (and returns to Chats if the open one empties), and removes the invisible full-screen layer the Allowed/Blocked/Deleted undo toast put over the list, which made the next tap on a row icon need a second press. Installed on the phone, not yet confirmed by Travis.
- Previous code commit `51300fa`. Working tree is clean apart from the untracked `.vscode/` folder.
- Back with a photo open in a conversation now redraws so one press closes the viewer (it used to need a second press, which left the conversation). Installed on the phone, not yet confirmed by Travis; if it still jumps to the list, ask whether he used the back key or the edge-swipe gesture.
- 2026-10-30 changes: pinch/pan/double-tap zoom in the photo viewer (and panning no longer triggers back-swipe); new **Unread** tab beside Chats/Updates/Screener; unread list removed from the home-screen widget; send arrow stays on screen when a long link is shared into New Message or typed in a conversation.
- The Unread tab, widget change and zoom were built and installed on the phone, but only the share-a-long-link fix was checked on the device. Check the rest by hand.
- Feature plans in `docs/superpowers/plans/` cover conversation filters, notifications, backup/restore, home widget, two-pane fold and computer access. Which of those are fully shipped has not been re-verified.

**Decisions:**
- 2026-10-30, Travis: unread chats live only in the Unread tab and return to Chats once read, instead of floating to the top of Chats or showing as bubbles. Unread chats in Updates/Screener stay in those tabs. The eye button in the header jumps to the Unread tab.
- 2026-10-30, Travis: the home-screen widget shows favorites only (no recent-unread rows) and always uses the compact layout.

**Open items:**
- Dead code left behind: `forgelink_widget_tall.xml`, `WidgetContent.RecentUnread` and its tests, and the `ShowUnreadOnly` property.
- The "Bubble unread" checkbox in Display settings now has no effect, since unread chats never appear in Chats. Remove it or repurpose it.
- Other `LookupAsync` callers (notifications, delivery status, MMS download, export) still pass raw addresses; blank ones are now safe, but no one has checked what they show for a thread with no address.
- Confirm the status of each plan listed above.

A native Android app that sends and receives real SMS text messages through
the phone's own cellular connection — not a chat app, no backend, no cloud
database. See `SMS Messenger App Requirements Document.html` for the full
product spec and `docs/superpowers/plans/2026-09-17-sms-messenger.md` for
the implementation plan this was built from.

## Requirements

- .NET SDK 10.0+ with the `android` workload (`dotnet workload install android`)
- A physical Android device connected over USB with USB debugging enabled.
  This project's development machine could not run the Android emulator at
  all (ARM64 Windows host — see the implementation plan's Task 1 for the
  full investigation); everything here is built and verified against a real
  device instead. An emulator may work fine on an x86_64 host if you have
  one, but is untested by this project.

## Build

    dotnet build src/ForgeLinkSms/ForgeLinkSms.csproj -f net10.0-android

(`dotnet build ForgeLinkSms.sln -f net10.0-android` — building the whole
solution with an Android target filter — does not work, since
`ForgeLinkSms.Core`/`ForgeLinkSms.Core.Tests` are plain `net10.0` projects
that don't target Android; build the app project directly, or build the
solution with no `-f` filter.)

## Run on a device

    dotnet build src/ForgeLinkSms/ForgeLinkSms.csproj -t:Run -f net10.0-android

## Run the unit tests

    dotnet test tests/ForgeLinkSms.Core.Tests

Unit tests cover every model and view model in `ForgeLinkSms.Core` — none
of them touch the Android runtime, so they run on any machine with the .NET
SDK. The Android-specific service implementations under
`src/ForgeLinkSms/Platforms/Android/` call real platform APIs
(`SmsManager`, content providers, `ContactsContract`, `RoleManager`) and are
verified by hand on a physical device, per the manual verification steps in
each task of the implementation plan.

## What this app does and doesn't do

See `SMS Messenger App Requirements Document.html`, section 2 ("What Real
SMS Can & Can't Do") — SMS has no typing indicators, no cross-network read
receipts, no reactions, no edit/delete, and no true shared group threads.
These are not missing features to add later; they don't exist in the SMS
protocol.

## The menu (top-right icon on Conversations)

Tapping the top-right icon on Conversations (your profile photo once you've
set one, a gear icon until then) opens a menu with:

- **Theme** — Light/Dark/System plus an accent color, applied instantly and
  remembered across restarts.
- **Your Profile** — a display name and photo, shown in the menu header and
  as the Conversations top-right icon.
- **Trash** — long-press a conversation in Conversations to trash it (the
  underlying messages are never deleted from Android's SMS provider, only
  hidden and recorded in a local SQLite table); restore it from here.
- **Blocked** — long-press a conversation to block it, which hides it
  locally right away and, on a best-effort basis, registers the number with
  Android's `BlockedNumberContract` so the OS also stops delivering future
  texts from it; if that OS-level registration fails for any reason, the
  app still falls back to its own local filtering. Unblock from here.
- **Mark as Read** — marks every conversation read in one action.
- **Undo** — reverses the single most recent Trash, Block, or Mark as Read
  action; press again to reverse the one before that. The undo stack is
  in-memory only and resets when the app restarts.

See `docs/superpowers/specs/2026-09-18-conversations-menu-design.md` and
`docs/superpowers/plans/2026-09-18-conversations-menu.md` for the design and
implementation plan this was built from.
