# Two-Pane Chats on the Unfolded Fold — Design

**Date:** 2026-09-30
**Status:** Approved in conversation; awaiting written-spec review

## Goal

When the Galaxy Z Fold is unfolded, show the chat list and the open chat side by side, the way a tablet messaging app does, while the folded cover screen keeps working exactly as today.

## Decisions (from the conversation)

- **Scope:** chats only. The chat list (`/conversations`) and a chat (`/conversations/thread`) share the screen. Every other page (Settings, Scheduled, Reminders, Archived, Compose, media, etc.) keeps opening full width.
- **Approach:** the split lives in the app's shared layout (`MainLayout`), not in a new combined page, so the two existing pages and every existing link into a chat (notifications, search, reminders, scheduled, splash routing) keep working unchanged.
- **No draggable divider** (fixed proportions).

## When two panes show

Two panes show when **both** are true:
- The viewport is at least **600 CSS px wide** (the unfolded inner screen is ~830 px; the cover screen is ~410 px).
- The current route is `/conversations` or `/conversations/thread` (query string ignored).

The rule is a pure function in Core (`TwoPaneRules.ShowsTwoPanes(double widthPx, string relativeUri)`) so it can be unit-tested. The layout learns the width from a small JS `matchMedia("(min-width: 600px)")` listener that calls back into .NET whenever it changes (unfolding/folding resizes the WebView; `MainActivity` already handles size changes without being recreated).

## Layout

- **Left pane:** the chat list, 40% of the width (minimum 320 px). The chat currently open on the right is highlighted in the list.
- **Right pane:** the routed page. On `/conversations` it shows a quiet placeholder: "Select a chat" with a short hint.
- **Divider:** a 1 px line in the theme's border color.
- Each pane is its own containing block (`transform: translateZ(0)` plus `overflow: hidden`), so everything a page positions as `fixed` — long-press selection bars, bottom sheets, the new-chat button, the chat's compose bar, image viewers — stays inside its own pane instead of spanning both.
- In one-pane mode the layout renders `@Body` exactly as today.

## Behavior

- **Tapping a chat** in the left pane navigates to that chat's route; the layout keeps the same list instance, so its scroll position and filters stay put, and only the right pane changes.
- **Back / the chat's back arrow** in two-pane mode returns to `/conversations`, leaving the list and the placeholder.
- **Folding** while a chat is open shows that chat full screen on the cover; folding on the list shows the list. **Unfolding** brings the list back on the left.
- **Keeping the list current:** the list refreshes when the open chat changes (so the unread badge clears), after sending from the right pane, and when messages arrive (existing incoming-message notifier).
- **Other pages** (from the menu, avatar, compose, media) render full width in both modes; returning to a chat restores the split.

## Components

**Core (unit-tested):**
- `Utils/TwoPaneRules.cs` — `ShowsTwoPanes(widthPx, relativeUri)` and `MinWidthPx = 600`.

**App:**
- `Components/Layout/MainLayout.razor` — decides one or two panes, renders `ConversationsPage` on the left and `@Body` (or the placeholder) on the right, and listens for width changes.
- `wwwroot/js` — `watchWidth(dotNetRef, minWidth)` using `matchMedia`, reporting the initial state and every change.
- `ConversationsPage` — highlights the open chat (read from the current route) and refreshes when the route or messages change; its rendering in one-pane mode is unchanged.
- `ThreadDetailPage` — tells the list to refresh after sending (through the existing incoming-message notifier or an equivalent event).

## Testing

- **Unit:** `TwoPaneRules` — below/at/above 600 px; `/conversations`, `/conversations/thread?id=…`, and other routes (`/settings`, `/conversations/media`, `/compose`) staying single.
- **On the phone (unfolded, with the user's help to open it):** list + placeholder; open a chat; switch chats; long-press selection and bottom sheets stay in the left pane; compose bar and image viewer stay in the right pane; Back closes the chat; fold with a chat open → full-screen chat on the cover; unfold → split returns; Settings opens full width. No texts are sent during testing except to the user's own number.

## Out of scope

- Two panes for menu pages (Settings, Scheduled, Reminders, etc.).
- A draggable divider or remembered pane width.
- Tablets and landscape phones beyond what the 600 px rule naturally covers.
