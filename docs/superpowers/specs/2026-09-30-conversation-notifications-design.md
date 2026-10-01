# Conversation Notifications and Android Auto — Design

**Date:** 2026-09-30
**Status:** Approved in conversation; awaiting written-spec review

## Goal

Make ForgeLink's message notifications first-class Android conversations — shown in the shade's Conversations section with contact photos and recent message history, eligible for Priority — and make them work in Android Auto (read aloud, voice reply, mark as read).

## Decisions (from the conversation)

- **Scope:** Conversations section + Priority + Android Auto. No chat bubbles.
- **Approach:** Android's conversation building blocks: `NotificationCompat.MessagingStyle` with `Person`s, one long-lived conversation shortcut per chat (`ShortcutManagerCompat.pushDynamicShortcut`), the notification linked to it (`setShortcutId`, `LocusId`), and semantic reply / mark-as-read actions for Android Auto.
- **ForgeLink's notification rules stay in charge.** `NotificationRules` (groups, quiet hours, codes), muting and the Screener decide *whether* and *how loudly* to notify exactly as today; this work only changes the notification's form and placement.

## Notification content

- **Messages shown:** the chat's unread incoming messages, newest last, capped at **5** (oldest dropped). If none are found (e.g. the provider hasn't caught up), the just-received text alone.
- **People:** the user is the `MessagingStyle` user ("You"). Each sender is a `Person` with the contact name (or formatted number), the contact photo as an icon when there is one, and a stable key (the normalized number) so Android can group per person.
- **Groups:** `setGroupConversation(true)`, conversation title = the group's name (the existing `GroupNames` formatting), each message attributed to its sender.
- **Text:** the message body; a reaction in its short form ("❤️ to “…”"); MMS without text as "📷 Photo" / "🎥 Video" / "📎 Attachment".
- **Reply from the notification:** after the reply is sent, the notification is re-posted with the reply appended as a message from "You" (Android otherwise shows a spinner forever), silently.
- **Mark as read:** unchanged behaviour (clears the notification).

## Conversation shortcuts

- When a notification is posted for a chat, ForgeLink pushes a dynamic, long-lived shortcut for that chat: id `chat-<threadId>`, short label = chat name, icon = contact photo (circle; initials fallback for groups/unknowns), intent = `MainActivity` with `initial_route=/conversations/thread?id=…&address=…`, category `android.shortcut.conversation`, the sender `Person`(s), `setLongLived(true)`, `LocusId("chat-<threadId>")`.
- The notification uses the same shortcut id and `LocusId`, which puts it in the Conversations section and lets the user long-press → Priority / Default / Silent.
- Dynamic shortcut limits are handled by `pushDynamicShortcut` (it evicts the least recently used).
- Deleting a chat removes its shortcut (`removeLongLivedShortcuts`).

## Android Auto

- `res/xml/automotive_app_desc.xml` with `<uses name="notification"/>`, referenced from the manifest by `com.google.android.gms.car.application` meta-data.
- The Reply action: `RemoteInput` (as today) + `setSemanticAction(SEMANTIC_ACTION_REPLY)` + `setShowsUserInterface(false)`.
- The Mark-as-read action: `setSemanticAction(SEMANTIC_ACTION_MARK_AS_READ)` + `setShowsUserInterface(false)`; Auto hides it but uses it after reading aloud.
- Both actions are added to the notification (Auto requires them); no new UI.

## Components

**Core (unit-tested):**
- `Utils/ConversationNotification.cs` — `ConversationNotification.Build(...)` takes the chat's recent messages (`SmsMessage` list), the chat name, whether it's a group, a name lookup for senders, and the just-received fallback, and returns `ConversationNotificationModel(string Title, bool IsGroup, IReadOnlyList<NotificationLine> Lines)` with `NotificationLine(string SenderKey, string SenderName, string Text, DateTimeOffset Time)`: unread incoming only, newest last, cap 5, reaction short form, attachment labels.

**Android:**
- `NotificationService.NotifyIncomingMessage` builds `MessagingStyle` from the model, sets the shortcut id / `LocusId`, adds the semantic actions; keeps the existing channels, sound/silent logic and notification ids.
- `ConversationShortcuts` — push and remove per-chat long-lived shortcuts.
- `NotificationActionReceiver` — after a reply, re-post the conversation with the sent reply appended.
- `IncomingMessagePipeline` — passes what the builder needs (thread id, sender, group label) — the decision logic is unchanged.
- Manifest meta-data + `automotive_app_desc.xml`.

## Errors

- Any failure building the conversation form (contact lookup, provider query, shortcut push) falls back to today's simple notification, so a text is never left un-notified.

## Testing

- **Unit:** the builder — unread-incoming filtering, ordering, the 5 cap, group attribution, fallback when no messages are found, reaction short form, attachment labels.
- **On the phone (needs the user to send a text):** the notification appears under Conversations with the sender's photo and the recent unread messages; long-press offers Priority; replying from the notification sends (to the user's own other phone — the user's choice of recipient) and the reply appears in the notification; Mark as read clears it; muted chats and quiet hours still behave as before.
- **Android Auto (optional, needs the user):** Desktop Head Unit on the PC with Android Auto developer mode on the phone — message read aloud, voice reply sent, marked read.

## Out of scope

- Chat bubbles.
- Changing *when* ForgeLink notifies (that stays with Settings → Notifications).
- Notifications for outgoing-message failures (unchanged).
