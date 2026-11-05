# In-app rating: plan

Spec: `docs/superpowers/specs/2026-11-05-in-app-rating-design.md`.

1. Website lib (`Cloud Compass Website/api/review_lib.php`): `ewo360_review_app_email`, `ewo360_review_is_app_review`, `ewo360_review_validate_app`; add `from_app` to `ewo360_review_public`. Tests in `tests/reviews/test_lib.php`.
2. Website store (`api/review_store.php`): `ewo360_review_submit_app` (rate limit, insert pending / update pending / exists). DB test in `tests/reviews/test_store_db.php` if a scratch MySQL is available.
3. Website endpoint `api/app_review_submit.php`, admin notification email, widget badge in `js/reviews.js` (+ cache-bust in `products/forgelink-sms.html`), docs in CLAUDE.md.
4. App Core: `IRatingSubmitter` + `RatingSubmitter` (HttpClient, injectable), `RatingPromptRules` additions; unit tests with a fake HTTP handler.
5. App: register service, install id store, `RatePage` submit/failure/thanks states, Chats-list card, menu row, `IReviewPromptService` additions (installer check, dismissed, rated).
6. Verify: PHP tests, `dotnet test`, build and install on the phone, drive `/rate` against a local `php -S` server.
7. Deploy the website files (confirm with Travis first), smoke-test live with one test review, then delete that test row from admin.
