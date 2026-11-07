---
name: test-assistant
description: Run the Ask ForgeLink assistant end-to-end tests (offline retrieval layer plus the live server layer), then diagnose and fix failures. Use before a release, after editing help text, the matcher, or the server prompt.
---

# Test the Ask ForgeLink assistant

The assistant answers in two steps. The app picks up to 3 help excerpts (`HelpMatcher`, `HelpCatalog`), then the server (`Cloud Compass Website/api/assistant_lib.php`, Gemini) answers from them. Test both.

## Layer 1: offline retrieval (free, seconds)

```
dotnet test tests/ForgeLinkSms.Core.Tests
```

`Assistant/AssistantRetrievalTests.cs` runs every question in `tests/ForgeLinkSms.Core.Tests/Assistant/questions.json` and checks:
- the expected help topic is among the 3 excerpts sent,
- the question is treated as about ForgeLink (so with Googling on it is not sent to a web search),
- the excerpts contain the words the answer needs,
- price, refund and privacy questions get the fixed answer, and how-to questions are not swallowed by it,
- everyday questions (weather, trivia) are not mistaken for ForgeLink questions.

## Layer 2: live server (spends the free daily allowance)

The server allows 60 requests per IP per day, shared with real use. A full run uses about 40. Run it at most once a day.

```
$env:FORGELINK_LIVE = "1"
dotnet test tests/ForgeLinkSms.Core.Tests --filter "FullyQualifiedName~AssistantLiveTests" --logger "console;verbosity=detailed"
```

It takes about 5 minutes (5 s between questions to stay under Gemini's rate limit), so run it in the background and read the PASS/FAIL lines when it finishes. It sends the questions flagged `"live": true`, checks the answers (must-contain words, no "not sure" for how-to questions, "not sure" for off-topic, injection attempts not followed), and stops with a clear message if the daily limit is hit; questions before that point are still valid.

## Reading failures

| Failure | Fix |
|---|---|
| Topic not among the 3 excerpts | Add a how-to tip to the topic in `HelpCatalog.cs` (tips max 90 characters, summary 70), or adjust `HelpMatcher` (stop words, synonyms). Never raise a topic's score by renaming it to a common word like "Messages". |
| Treated as a general question | Add keyword-rich tips so the best topic scores 2 or more (`AboutAppScore`). |
| Excerpts do not mention a needed word | Add that wording to the topic; tips come before icons in each excerpt and are cut at 900 characters. |
| Fixed answer wrongly used | Tighten the phrase in `AssistantFixedAnswers.cs`. Remember phrases match as substrings (use a leading space). |
| Live: said it was not sure | Check what excerpts were sent, then the prompt in `assistant_lib.php`. Repeat the question 5 times before calling it a bug; the model is slightly random. |
| Live: server did not answer | Usually the daily limit (HTTP 429) or Gemini's rate limit. Wait, do not edit code. |

Server prompt changes deploy with `scripts/deploy-hostgator.sh --target cloudcompassai api/assistant_lib.php` from the Cloud Compass Website repo. App help changes need an app release.

## Adding a question

Add an entry to `questions.json`: `q`, `kind` (howto, fixed, offtopic, inject), `topic` (help topic title, `A|B` for alternatives), `mustContain` (each entry is alternatives `a|b`; the answer needs one word from every entry), `mustNotContain`, and `live` (true to also run it against the server). A "not sure" report email from a user is a good source of new questions.

## Report

After a clean live run, publish a private results page (pass/fail by kind, each failure's question and answer) with the Artifact tool if the user asks.
