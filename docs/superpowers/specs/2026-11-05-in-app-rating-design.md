# In-app rating that feeds the product page reviews

2026-11-05. Travis asked for a Google-style 5-star rating with a message inside ForgeLink SMS, sent to the cloudcompassai website, stored in its database and shown on the products page as reviews from real users. He chose "skip the questions, build it, somewhat seamless", so the choices below are my calls.

## What already exists
- App: `/rate` page (5 stars + comment) that only opens an email draft; a Google Play in-app review prompt (`ReviewPromptService`) that Google never shows for sideloaded installs, which is how website buyers install.
- Website: a full reviews system (`api/review_*.php`, `js/reviews.js`, `admin/reviews.php`, table `product_reviews`) with moderation. The product page `products/forgelink-sms.html` already renders approved reviews.

## Design
**Website (no schema change).**
- New public endpoint `api/app_review_submit.php`. POST form fields: `product`, `install_id` (random UUID made by the app), `stars` 1-5, `comment` (optional, max 1500), `name` (optional, max 40), `consent=1`, `app_version`.
- Stored in `product_reviews` as `pending` (so it waits in `admin/reviews.php` for approval, same as every review; moderated on content, never on stars). The one-review-per-email unique key is reused with a synthetic key `app-<sha256(install_id) first 32 hex>@app.invalid`, so each install has one review. While still pending the person can change it by sending again; once approved or rejected a resend changes nothing.
- No email is collected or sent. Title is generated ("Rated N stars"), display name defaults to "ForgeLink user", `verified_buyer` stays 0 (the app cannot prove a purchase).
- Same rate limit as web reviews (3 per IP per hour), plus an admin notification email to the existing `notify_email`.
- Public review JSON gets `from_app`; the widget shows a "From the app" badge so visitors can tell these are in-app ratings. Reviews with an empty message show stars only.

**App.**
- `RatePage` sends the rating to the site (stars, optional message, optional name) with a one-line note that it may appear on cloudcompassai.com and that no phone number or contacts are sent. On failure it keeps what was typed and offers "Send by email instead" (the old behavior).
- Seamless prompt: on the Chats list, for installs not from Google Play (Play installs keep Google's own card), once `ReviewPromptRules` is satisfied (7 days, 20 messages sent) and the person has not rated, a slim card "Enjoying ForgeLink?" with five tappable stars. Tapping a star opens `/rate` with that many stars selected. "Not now" hides it for 120 days. After a successful send it never appears again.
- A "Rate ForgeLink" row in the menu opens `/rate` any time.

## Non-goals / anti-goals
- No auto-approval, no review gating (everyone who qualifies is asked, and low ratings are not hidden).
- No collecting phone numbers, contacts or message content. Install id is random, not a device id.
- No editing or deleting a review from the app after approval.

## Risks
- Anyone can POST to the endpoint; protection is pending-by-default moderation, the IP rate limit and install-id format check. A determined spammer can still fill the moderation queue.
- Deploying the endpoint changes the live site; the app build that calls it must not ship before the endpoint is live.
