# ForgeLink help assistant (2026-11-05)

Decided by Travis 2026-11-05: full-screen chat (mockup "Option B"), free of charge, "pick the best scope, no more questions". Scope picked by Claude: how-to questions and troubleshooting, with price/refund/privacy as fixed text.

## Goal
A chat screen (Menu > Ask ForgeLink) that answers how-to and "something is not working" questions about ForgeLink, using only the app's own help text. Costs nothing to Travis or users.

## Non-goals / anti-goals
- Never sees message content, contacts, phone numbers or anything but what the user types in the chat box.
- Never promises a feature, price, refund or privacy behavior that is not fixed text. Never diagnoses a carrier problem as an app bug.
- No paid API and no billing attached to the Google project. No accounts, no chat history stored on the server.

## Behavior
1. Fixed answers first (`AssistantFixedAnswers`): price/subscription, refund, privacy/"does it read my messages", who made it. Pure text, never sent anywhere.
2. Otherwise `HelpMatcher` picks the 1 to 4 best help topics from `HelpCatalog` by keyword score and builds excerpts.
3. `AssistantClient` POSTs `{install_id, question, excerpts[], app_version}` to `https://cloudcompassai.com/api/assistant.php` (15 s timeout).
4. Server (`api/assistant.php`) validates and size-limits the request, enforces limits (per install 20/day, per IP 60/day, all users 300/day, configurable, kept below Gemini's free quota), then calls the Gemini REST API (`generateContent`, key from `api/assistant_config.php` or `GEMINI_API_KEY`, default model `gemini-2.5-flash-lite`, configurable) with a system prompt: answer only from the excerpts, plain short steps, say "I don't know" and point to Send a report when the excerpts do not cover it.
5. Any failure (offline, 429/limit, server 503, placeholder key, timeout) falls back to the best local help topic shown as an answer, labeled "Answered from the help pages", plus a Send a report button (existing crash report email flow). Never an error wall.
6. Every answer shows its source: the help topic title it used.

## Privacy
Chat text and install id (random, same as ratings) go to cloudcompassai.com, then the question and excerpts go to Google's Gemini API. Nothing is stored server-side except usage counters (install id hash, IP, date). Privacy policy updated; chat screen carries a "Don't type private information" note. Unconfirmed: whether Google may use free-tier prompts to improve its products; Travis should read Google's terms before enabling the key.

## Constraints
Stack per Travis: raw REST, no SDK, key from env/config (gitignored, web-denied, fail closed on `REPLACE_ME`), MAUI Blazor Hybrid, simplest thing that works. Server limits use the existing admin MySQL DB (table `assistant_usage`, created lazily).

## Left to the builder
Matching heuristic details, exact copy, CSS. Models/limits are config, not code.
