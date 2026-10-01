# Home Screen Widget — Design

**Date:** 2026-09-30
**Status:** Approved in conversation; awaiting written-spec review

## Goal

A ForgeLink home screen widget that shows favorite people with unread badges, the total unread count, and (when the widget is tall enough) the latest unread chats, so people can see and open what matters without opening the app first.

## Decisions (from the conversation)

- **Content:** favorites + unread (option chosen over "count only" and "recent chats list").
- **Approach:** a standard Android app widget (`AppWidgetProvider` + `RemoteViews` layouts) with a fixed layout; a pure Core builder decides what to show. No scrolling collection widget, no Glance (not available to .NET).
- **Privacy:** a new Settings → Privacy switch, "Show message text in the home screen widget" (on by default). Off = names and counts only. (ForgeLink has no "hide notification content" setting to follow.)

## What the widget shows

- **Header:** "ForgeLink" and the total unread count across chats that appear in the Chats list (the same set the app counts: not archived, trashed, snoozed or blocked), plus a ✏️ button that opens Compose.
- **Favorites row:** up to 5 favorites. Order: those with unread messages first (most unread first, then newest), then the rest by most recent message. Each shows the contact photo cropped to a circle (or initials on the accent color) with an unread badge when > 0, and the first name below. Tap → that chat.
- **Recent unread (tall widget only):** up to 3 chats with unread messages, newest first. All unread chats are eligible, so a favorite can appear in both the row and this list, since this list is about reading the latest. Each row: name, preview (or "New message" when message text is turned off; reactions in their short form "❤️ to “…”"), and time ("4:12 PM" today, "Sep 27" earlier). Tap → that chat.
- **Empty states:** no favorites → the row says "Heart a chat to pin it here"; nothing unread → the list area says "All caught up".
- **Sizes:** minimum 4×1 cells (header + favorites); 4×2 or taller adds the recent-unread rows. Android 12+ picks the layout by size automatically; older versions pick it in `OnAppWidgetOptionsChanged`.
- **Look:** rounded card; background and text follow the phone's light/dark mode; badges and the ✏️ use the app's accent color.

## Keeping it current

The widget is refreshed (all widget instances, via one `WidgetUpdater.RequestUpdate(context)` call) when:
- a text arrives (`IncomingMessagePipeline`), including while the app is closed;
- a chat is read or a message is sent in the app (the existing `ConversationListRefresher` / mark-as-read paths);
- favorites change, or a chat is archived, deleted, snoozed or blocked;
- the app goes to the background (catch-all);
- Android's periodic update (`updatePeriodMillis` = 30 minutes) as a safety net.

Refreshes run on a background thread and coalesce (at most one rebuild in flight; a request during a rebuild schedules one more).

## Taps

Each tap is a `PendingIntent` to `MainActivity` with the existing `initial_route` extra (the path notifications use), so it works from a cold start:
- favorite / unread row → `/conversations/thread?id=…&address=…`
- ✏️ → `/compose`
- header → `/conversations`

## Components

**Core (unit-tested):**
- `Utils/WidgetContent.cs` — `WidgetContent.Build(IReadOnlyList<SmsThread> threads, bool showText, DateTime todayLocal)` → `WidgetSnapshot(int TotalUnread, IReadOnlyList<WidgetPerson> Favorites, IReadOnlyList<WidgetRow> RecentUnread)`; `WidgetPerson(long ThreadId, string Address, string Name, string Initials, string? PhotoUri, int Unread)`; `WidgetRow(long ThreadId, string Address, string Name, string Text, string When)`.
- `DisplaySettings.ShowWidgetText` (default true).

**Android:**
- `Platforms/Android/Widget/ForgeLinkWidgetProvider.cs` — `AppWidgetProvider` (`[BroadcastReceiver]` + `[IntentFilter(APPWIDGET_UPDATE)]` + `[MetaData("android.appwidget.provider", Resource = "@xml/forgelink_widget_info")]`); `OnUpdate`/`OnAppWidgetOptionsChanged` → `WidgetUpdater`.
- `Platforms/Android/Widget/WidgetUpdater.cs` — loads threads (the same filtering the Chats list uses), builds the snapshot, renders `RemoteViews` (circle-cropped photos ≤ 96 px, initials bitmaps), sets tap intents, pushes to all instances.
- `Platforms/Android/Resources/layout/forgelink_widget_small.xml`, `forgelink_widget_tall.xml`; `xml/forgelink_widget_info.xml` (min size, resize mode, preview, `updatePeriodMillis`); drawables for the card and badge; `values` / `values-night` colors.
- Settings → Privacy switch; refresh hooks at the points listed above.

## Errors

- Any failure while building the widget (no SMS permission, provider error) shows the header with "Open ForgeLink" and leaves taps working; it never crashes the receiver.
- A contact photo that can't be loaded falls back to initials.

## Testing

- **Unit:** favorite ordering and the 5 cap; total unread excludes nothing the Chats list includes and includes nothing it excludes (the builder receives the already-filtered list); recent-unread newest-first and the 3 cap; `showText: false` → "New message"; reactions shown in short form; time formatting today vs earlier; empty states.
- **On the phone:** add the widget (cover screen), check names/badges/total against the app; tap a favorite, a row, ✏️; resize 4×1 ↔ 4×2; turn off "Show message text" → previews hidden; ask the user to send a text to the phone (nothing is sent from it) → widget updates.

## Out of scope

- Replying from the widget.
- A scrolling list of all chats.
- Per-widget configuration (choosing which people to show).
