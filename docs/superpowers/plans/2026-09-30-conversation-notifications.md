# Conversation Notifications and Android Auto Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Message notifications become Android conversations (MessagingStyle with people and recent unread messages, linked to a per-chat long-lived shortcut so they appear under Conversations and can be made Priority) and work in Android Auto.

**Architecture:** A Core builder turns a chat's recent messages + unread count into a notification model. `NotificationService` renders it as `MessagingStyle`, pushes/uses a conversation shortcut, and tags the existing Reply/Mark-read actions with Auto's semantic actions. Manifest declares Auto messaging support. All existing "whether to notify" logic is untouched.

**Tech Stack:** .NET 10 MAUI Android, AndroidX Core (NotificationCompat, ShortcutManagerCompat, Person), xUnit.

**Spec:** `docs/superpowers/specs/2026-09-30-conversation-notifications-design.md`

## Global Constraints

- Up to 5 unread incoming messages per notification; newest last.
- Shortcut id and LocusId `chat-<threadId>`; long-lived; category `android.shortcut.conversation`; intent = `MainActivity` + `initial_route`.
- Reply: `SEMANTIC_ACTION_REPLY`, `setShowsUserInterface(false)`; Mark read: `SEMANTIC_ACTION_MARK_AS_READ`, `setShowsUserInterface(false)`.
- `automotive_app_desc.xml` `<uses name="notification"/>` + manifest meta-data `com.google.android.gms.car.application`.
- Any failure building the conversation form falls back to today's plain notification.
- Existing notify/sound decisions, channels and notification ids unchanged.
- No commits unless asked; no comments unless the WHY is non-obvious; device testing sends nothing unless the user agrees.

## Review Focus

1. A text in a muted chat or during quiet hours must behave exactly as before (decision path untouched — Task 3 only changes the render).
2. Group chats: each line attributed to the right sender; the user's own messages never appear as unread.
3. Reply from the notification (and Android Auto voice reply) must still send, mark read and clear the notification.
4. Shortcut churn: posting many notifications must not throw when the dynamic-shortcut limit is reached (`pushDynamicShortcut` evicts).
5. Contact photos as `data:` URIs (the app's format) must become notification/shortcut icons; failures fall back to no icon.

---

### Task 1: `ConversationNotification` builder

**Files:** Create `src/ForgeLinkSms.Core/Utils/ConversationNotification.cs`; Test `tests/ForgeLinkSms.Core.Tests/Utils/ConversationNotificationTests.cs`

**Interfaces — Produces:** `record NotificationLine(string SenderKey, string SenderName, string Text, DateTimeOffset Time)`; `record ConversationNotificationModel(string Title, bool IsGroup, IReadOnlyList<NotificationLine> Lines)`; `static ConversationNotificationModel ConversationNotification.Build(IReadOnlyList<SmsMessage> recentOldestFirst, int unreadCount, string title, bool isGroup, Func<string, string> nameFor, NotificationLine fallback)`; `const int MaxLines = 5`.

Rules: take incoming messages (not outgoing, not hidden reactions) from the end, `min(unreadCount, 5)` of them (at least 1 when unreadCount ≥ 1), oldest first; text = `ReactionParser.Describe(body) ?? body`, or "📷 Photo" (image/gif), "🎥 Video", "🎤 Voice message", "📎 Attachment" when the body is empty; SenderKey = `PhoneNumberFormatter.ToComparableDigits(address)` (or the address); SenderName = `nameFor(address)`; empty result → `[fallback]`.

- [ ] Tests (write first): unread filtering (outgoing and hidden reactions skipped; only the last `unreadCount` incoming), the 5 cap, ordering, group attribution via `nameFor`, attachment labels, reaction short form, fallback when nothing matches, unreadCount 0 → fallback.
- [ ] RED → implement → GREEN; full suite green.

### Task 2: Conversation shortcuts and icons

**Files:** Create `src/ForgeLinkSms/Platforms/Android/ConversationShortcuts.cs`; reuse `Widget/WidgetBitmaps.Circle` for icons (make it `internal` reachable, already internal in the same assembly).

- [ ] `static string Push(Context, long threadId, string address, string name, IReadOnlyList<AndroidX.Core.App.Person> people, Bitmap? icon)` — builds `ShortcutInfoCompat.Builder(context, $"chat-{threadId}")` with short label, `IconCompat.CreateWithAdaptiveBitmap` or `CreateWithBitmap` (initials fallback), intent (`ActionView`, MainActivity, `initial_route`), `SetLongLived(true)`, `SetLocusId(new LocusIdCompat(id))`, `SetPersons(people)`, categories `{ "android.shortcut.conversation" }`; `ShortcutManagerCompat.PushDynamicShortcut`; returns the id. Exceptions are caught and logged; returns null on failure.
- [ ] `static void Remove(Context, long threadId)` → `ShortcutManagerCompat.RemoveLongLivedShortcuts(context, [id])`; call it where a chat is permanently deleted (`ThreadDeletionService`).
- [ ] Release build.

### Task 3: MessagingStyle notifications and Android Auto

**Files:** Modify `src/ForgeLinkSms/Platforms/Android/NotificationService.cs`, `src/ForgeLinkSms.Core/Services/INotificationService.cs` (add `NotifyConversation(long threadId, string address, string fallbackTitle, string fallbackBody, bool withSound)`), `src/ForgeLinkSms/Platforms/Android/IncomingMessagePipeline.cs` (call `NotifyConversation` instead of `NotifyIncomingMessage` for real threads), `src/ForgeLinkSms/Platforms/Android/AndroidManifest.xml`; Create `src/ForgeLinkSms/Platforms/Android/Resources/xml/automotive_app_desc.xml`.

- [ ] `NotifyConversation`: load the thread (`IThreadService`) for name/unread/group participants, the last 20 messages (`ISmsService.GetMessagesAsync(threadId, null, 20)`, reversed to oldest-first), contact names/photos (`IContactService`), build the model (Task 1), then:
  - `var me = new Person.Builder().SetName("You").Build();` `var style = new NotificationCompat.MessagingStyle(me)`; group → `SetConversationTitle(title)`, `SetGroupConversation(true)`.
  - each line → `style.AddMessage(new NotificationCompat.MessagingStyle.Message(text, time.ToUnixTimeMilliseconds(), person))` with one cached `Person` per SenderKey (`SetKey`, `SetName`, `SetIcon` from `WidgetBitmaps.Circle` when a photo exists).
  - shortcut id from Task 2 → `builder.SetShortcutId(id)`, `SetLocusId(new LocusIdCompat(id))`; `SetCategory(NotificationCompat.CategoryMessage)`.
  - existing channel/silent/content intent/notification id; Reply action gets `.SetSemanticAction(NotificationCompat.Action.SemanticActionReply).SetShowsUserInterface(false)`; Mark-read action built with `NotificationCompat.Action.Builder` + `.SetSemanticAction(SemanticActionMarkAsRead).SetShowsUserInterface(false)`.
  - any exception → log and call the existing `NotifyIncomingMessage(fallbackTitle, fallbackBody, …)`.
- [ ] `automotive_app_desc.xml`: `<automotiveApp><uses name="notification" /></automotiveApp>`; manifest `<meta-data android:name="com.google.android.gms.car.application" android:resource="@xml/automotive_app_desc" />` inside `<application>`.
- [ ] Release build; Core suite.

### Task 4: Device verification (needs the user)

- [ ] Install. Ask the user to text the phone from another phone (twice, a few seconds apart).
- [ ] `adb shell dumpsys notification --noredact` shows the ForgeLink notification with `android.messagingStyleUser`/`android.messages` and `shortcutId=chat-<id>`; screenshot of the shade shows it under **Conversations** with the photo and both messages.
- [ ] Ask the user to long-press the notification → Priority option present.
- [ ] Mark as read from the notification → it clears and the chat is read.
- [ ] Reply from the notification only with the user's go-ahead (it texts their other phone) → sends, notification clears.
- [ ] Optional: Android Auto Desktop Head Unit steps for the user.
