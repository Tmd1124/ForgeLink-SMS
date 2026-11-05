# Help assistant plan (2026-11-05)
Spec: `docs/superpowers/specs/2026-11-05-help-assistant-design.md`

## Website repo (Cloud Compass Website)
1. `api/assistant_lib.php` pure functions + `tests/assistant/test_lib.php`: request validation, prompt build, Gemini response parse, limit decision.
2. `api/assistant_config.example.php`, gitignore + `api/.htaccess` deny for `assistant_config.php`.
3. `api/assistant.php` endpoint (POST JSON, fail closed, lazy `assistant_usage` table, curl to Gemini). Local test with a stub.
4. Deploy files to cloudcompassai (endpoint answers 503 until the key is set, which the app treats as fallback).

## App repo (ForgeLink SMS)
1. Core + tests (TDD): `HelpMatcher`, `AssistantFixedAnswers`, `AssistantClient`, `AssistantService`.
2. `Platforms/Android/AssistantInstallId` reuse of the rating install id via DI func; register in `MauiProgram`.
3. `Pages/Assistant/AssistantPage.razor` (route `/assistant`), Menu row "Ask ForgeLink", styles.
4. Privacy policy text (both repos), README "Pick up here".
5. Unit tests all pass; build debug APK; check on phone is Travis's step.

## Needs Travis
Create a Gemini API key in Google AI Studio on a project WITHOUT billing, put it in the server's `api/assistant_config.php`. Release via `scripts\release.ps1` after bumping the version.
