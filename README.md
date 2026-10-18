# ForgeLink SMS

## Pick up here

*Last updated 2026-10-18.*

**Run it:** connect a physical Android device over USB (the emulator does not run on this ARM64 machine), check `adb devices`, then `dotnet build src/ForgeLinkSms/ForgeLinkSms.csproj -t:Run -f net9.0-android`. Unit tests: `dotnet test tests/ForgeLinkSms.Core.Tests`.

**Deploy:** CI is `azure-pipelines.yml`. Push to both remotes (`origin` = GitHub, `azuredevops`).

**State of play:**
- Recent work (latest commit `b7602e9`) is UI polish: conversation list scrolling, screen slide transitions, New Message sheet, contact thumbnails, 3-second undo-send window.
- Feature plans in `docs/superpowers/plans/` cover conversation filters, notifications, backup/restore, home widget, two-pane fold and computer access. Which of those are fully shipped was not re-verified on 2026-10-18.
- Uncommitted: only the untracked `.vscode/` folder.

**Decisions:** none recorded here; see the specs and plans in `docs/superpowers/`.

**Open items:** confirm the status of each plan listed above.

A native Android app that sends and receives real SMS text messages through
the phone's own cellular connection — not a chat app, no backend, no cloud
database. See `SMS Messenger App Requirements Document.html` for the full
product spec and `docs/superpowers/plans/2026-09-17-sms-messenger.md` for
the implementation plan this was built from.

## Requirements

- .NET SDK 9.0+ with the `android` workload (`dotnet workload install android`)
- A physical Android device connected over USB with USB debugging enabled.
  This project's development machine could not run the Android emulator at
  all (ARM64 Windows host — see the implementation plan's Task 1 for the
  full investigation); everything here is built and verified against a real
  device instead. An emulator may work fine on an x86_64 host if you have
  one, but is untested by this project.

## Build

    dotnet build src/ForgeLinkSms/ForgeLinkSms.csproj -f net9.0-android

(`dotnet build ForgeLinkSms.sln -f net9.0-android` — building the whole
solution with an Android target filter — does not work, since
`ForgeLinkSms.Core`/`ForgeLinkSms.Core.Tests` are plain `net9.0` projects
that don't target Android; build the app project directly, or build the
solution with no `-f` filter.)

## Run on a device

    dotnet build src/ForgeLinkSms/ForgeLinkSms.csproj -t:Run -f net9.0-android

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
