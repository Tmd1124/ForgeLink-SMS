# ForgeLink SMS — Google Play Console answers

Copy these into the matching Play Console pages. Anything in `CAPS` is yours to fill in.

## Upload

- File: `src/ForgeLinkSms/bin/Release/net10.0-android/publish/com.forgelink.sms-Signed.aab`
  (build it with `dotnet publish -c Release -f net10.0-android` from `src/ForgeLinkSms`).
- Package name: `com.forgelink.sms` (permanent).
- When asked, **opt in to Play App Signing**. Google keeps the app signing key; you keep the
  upload key in `C:\Users\Tmd11\ForgeLink-Keys\`. Back up that whole folder (e.g. to a password
  manager or an encrypted USB drive). If it is lost, Google can reset the upload key, but it
  takes days.
- Every new upload needs a higher version: raise `ApplicationVersion` (1, 2, 3, …) and
  `ApplicationDisplayVersion` (1.0, 1.1, …) in `ForgeLinkSms.csproj`.

## Store listing

**App name** (30 max): `ForgeLink SMS`

**Short description** (80 max):
`Fast, private texting with smart lanes, scheduled sends, and a spam screener.`

**Full description** (4000 max):

```
ForgeLink SMS is a fresh take on text messaging — built to keep the people you care about up front and everything else out of the way.

KEEP YOUR PEOPLE UP FRONT
• Bubble Pond puts the people you text most at the top of your chats, one tap away
• Updates lane automatically groups codes, deliveries, banks and store texts by topic
• Screener holds texts from unknown numbers so they never interrupt you

TEXT THE WAY YOU WANT
• Picture and group messages (MMS), with real group conversations
• Voice messages, photos, videos, contacts and your location
• Reactions, reply-with-quote, and link previews
• Quick replies for the things you say all the time
• Schedule a text — even to a group — to send later
• Drafts are saved automatically in every conversation

STAY IN CONTROL
• Reply or mark as read right from the notification
• Mute conversations for an hour, a day, or until you turn them back on
• Snooze a conversation and have it come back when you're ready
• Archive, trash, block, and custom filters
• Search every conversation, or search inside one
• Photos & links view for each conversation

PRIVATE BY DESIGN
Your messages and contacts stay on your phone. ForgeLink SMS has no ads, no tracking, and no accounts. Nothing is uploaded to us.

ForgeLink SMS works as your default SMS app. Standard carrier messaging rates apply.
```

**App icon**: `images/ForgeLink Play Store Icon 512.png`
**Feature graphic**: `images/ForgeLink Feature Graphic 1024x500.png`
**Phone screenshots**: 2–8 required (see "Screenshots" below).
**Category**: Communication
**Contact email**: `Tmd1124@outlook.com`
**Privacy policy URL**: where you host `docs/play-store/privacy-policy.html`
(e.g. `https://azureforgeai.com/forgelink/privacy`).

## Screenshots

Play needs at least 2 phone screenshots (1080×1920 or larger works). Real conversations would
show your contacts' names, numbers and messages, so take them with test content only, e.g.:

1. Chats with the Bubble Pond (use a few test contacts)
2. A conversation with photos and reactions
3. The Updates lane
4. Scheduling a message

## App content

**Privacy policy**: the URL above.

**Ads**: No, the app does not contain ads.

**App access**: All functionality is available without special access. (Note for reviewers:
the app must be set as the default SMS app on first launch.)

**Content rating** questionnaire — category **Communication / social**. Answers:
- Violence, sexual content, profanity, drugs, gambling: No.
- Users can interact or exchange content: **Yes** (it's a messaging app).
- Shares user location with other users: **Yes** — only when the user sends their location in a message.
- Digital purchases: No.
Expected result: Everyone / PEGI 3 with "Users Interact", "Shares Location".

**Target audience**: 13+ (or 18+). Do **not** select any age group under 13.

**News app**: No. **COVID-19 contact tracing**: No. **Government app**: No.
**Financial features**: None.

## Data safety

The app sends no data to the developer. Messages go user → carrier → recipient.

- Does your app collect or share any of the required user data types? **No.**
- Is all user data encrypted in transit? Answer only if asked; with "No data collected" it is
  not required.
- Account creation: **My app does not allow users to create an account.**
- Data deletion URL: not required (no data collected).

Why "No": Google's definition of *collected* is data transmitted off the device by the app to
the developer or a third party. ForgeLink stores everything on the phone. User-initiated
messages to other people (SMS/MMS through the carrier) and link-preview page visits are not
collection by the developer.

## Sensitive permissions — SMS permission declaration

Play Console → App content → **Sensitive app permissions** → SMS and Call Log.

- **Core functionality**: select **Default SMS handler**.
- Permissions used: `READ_SMS`, `SEND_SMS`, `RECEIVE_SMS`, `RECEIVE_MMS`, `RECEIVE_WAP_PUSH`.
- **Description** (paste):

```
ForgeLink SMS is a full replacement messaging app. It asks the user to make it the default SMS handler on first launch and only then requests SMS permissions. As the default handler it reads, sends and receives SMS and MMS so the user can read their conversations, send and receive text, picture and group messages, schedule messages, reply from notifications, and search their messages. Message data never leaves the device except when the user sends a message through their carrier.
```

- **Video** (required): record the phone screen (Samsung: swipe down → Screen recorder),
  about 30–60 seconds, upload unlisted to YouTube, and paste the link:
  1. Fresh install → open ForgeLink SMS → setup screen.
  2. Tap "1. Make this my default SMS app" → accept the Android prompt.
  3. Tap "2. Grant permissions" → allow SMS/Contacts/Phone/Notifications → Continue.
  4. Show the conversation list, open a conversation, send a text, and receive one.

## Location permission

Location is requested only when the user taps **Share location** in the attachment drawer
(foreground only, no background location). No declaration form is needed for foreground
location; the data safety answer stays "No" because it is only sent inside the user's own
message.

## Before you can publish (account)

- **Personal developer account** (created after Nov 13, 2023): run a **closed test with at
  least 12 testers who stay opted in for 14 days in a row** before you can apply for production.
- **Organization account**: needs a D-U-N-S number; no 12-tester requirement.
- **To sell the app**: set up a payments profile in Play Console (Setup → Payments profile),
  then set a price under Monetize → Products → App pricing. A free app can never be changed to
  paid later, so set the price before the first production release.
