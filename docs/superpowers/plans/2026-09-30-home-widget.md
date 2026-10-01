# Home Screen Widget Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A resizable ForgeLink home screen widget showing favorites with unread badges, the total unread count, a new-message button, and (when tall) the 3 latest unread chats.

**Architecture:** A pure Core builder (`WidgetContent.Build`) turns the Chats-list threads into a `WidgetSnapshot`. An Android `AppWidgetProvider` and a `WidgetUpdater` load threads through the existing `ConversationsViewModel` (so filtering matches the Chats list exactly), render fixed `RemoteViews` layouts (small and tall, chosen by the widget's height), and attach `initial_route` tap intents. Refresh hooks call `WidgetUpdater.RequestUpdate`.

**Tech Stack:** .NET 10 MAUI (Android), Android AppWidget/RemoteViews, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-30-home-widget-design.md`

## Global Constraints

- Min Android API 24 (`SupportedOSPlatformVersion` 24.0): anything API 31+ (badge tint via `setBackgroundTintList`) needs a version check with a working fallback.
- Favorites row ≤ 5; recent unread ≤ 3; minimum size 4×1, tall layout at ≥ 110 dp height; `updatePeriodMillis` 1800000.
- Totals and lists come from the Chats tab (Conversations lane) after the app's usual filtering (archived, trashed, snoozed, blocked excluded).
- "Show message text in the home screen widget" (Settings → Privacy) default on; off → "New message".
- Taps use `MainActivity` + `initial_route` extra; favorite/row → `/conversations/thread?id=…&address=…`, ✏️ → `/compose`, header → `/conversations`.
- Widget failures never crash the receiver: show "Open ForgeLink".
- No commits unless the user asks; no comments unless the WHY is non-obvious; device testing sends no texts.

## Review Focus

1. Receiver context: the widget update may run when the app process just started for a broadcast — services from `MauiApplication.Current.Services` must be ready and the work must not block the main thread (Task 2 uses `GoAsync` + `Task.Run`).
2. Many quick refresh requests (a burst of texts) must not stack parallel rebuilds (Task 2 coalescing).
3. RemoteViews size limits: bitmaps must be small (≤ 96 px circles) or the update is rejected silently (Task 2).
4. A favorite whose chat is archived/snoozed/blocked must not appear (the builder only sees the filtered Chats list; test in Task 1).
5. Unread totals must match what the app shows after reading a chat (refresh hook on mark-as-read; device check in Task 4).

---

### Task 1: `WidgetContent` builder and the privacy setting

**Files:**
- Create: `src/ForgeLinkSms.Core/Utils/WidgetContent.cs`
- Modify: `src/ForgeLinkSms.Core/Models/DisplaySettings.cs` (add `public bool ShowWidgetText { get; set; } = true;` with a one-line doc comment)
- Test: `tests/ForgeLinkSms.Core.Tests/Utils/WidgetContentTests.cs`

**Interfaces:**
- Produces: `record WidgetPerson(long ThreadId, string Address, string Name, string Initials, string? PhotoUri, int Unread)`, `record WidgetRow(long ThreadId, string Address, string Name, string Text, string When)`, `record WidgetSnapshot(int TotalUnread, IReadOnlyList<WidgetPerson> Favorites, IReadOnlyList<WidgetRow> RecentUnread)`, `static WidgetSnapshot WidgetContent.Build(IReadOnlyList<SmsThread> chats, bool showText, DateTime todayLocal)`, constants `MaxFavorites = 5`, `MaxRecent = 3`.

- [ ] **Step 1: Failing tests**

```csharp
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Tests.Utils;

public class WidgetContentTests
{
    private static readonly DateTime Today = new(2026, 9, 30);

    private static SmsThread Chat(long id, string name, int unread = 0, bool favorite = false, int hoursAgo = 1, string body = "hi") => new()
    {
        Id = id,
        Address = $"555000{id:0000}",
        DisplayName = name,
        LastMessageBody = body,
        LastMessageTimestamp = new DateTimeOffset(Today.AddHours(12 - hoursAgo), TimeZoneInfo.Local.GetUtcOffset(Today)),
        UnreadCount = unread,
        IsFavorite = favorite
    };

    [Fact]
    public void Totals_unread_across_the_chats_it_is_given()
    {
        var snapshot = WidgetContent.Build(new[] { Chat(1, "A", unread: 2), Chat(2, "B", unread: 3), Chat(3, "C") }, true, Today);

        Assert.Equal(5, snapshot.TotalUnread);
    }

    [Fact]
    public void Favorites_with_unread_come_first_then_the_most_recent_and_stop_at_five()
    {
        var chats = new[]
        {
            Chat(1, "Old fav", favorite: true, hoursAgo: 9),
            Chat(2, "Two unread", unread: 2, favorite: true, hoursAgo: 5),
            Chat(3, "Not fav", unread: 9),
            Chat(4, "Recent fav", favorite: true, hoursAgo: 1),
            Chat(5, "Five unread", unread: 5, favorite: true, hoursAgo: 8),
            Chat(6, "F6", favorite: true, hoursAgo: 2),
            Chat(7, "F7", favorite: true, hoursAgo: 3),
        };

        var names = WidgetContent.Build(chats, true, Today).Favorites.Select(f => f.Name);

        Assert.Equal(new[] { "Five unread", "Two unread", "Recent fav", "F6", "F7" }, names);
    }

    [Fact]
    public void Recent_unread_lists_the_three_newest_unread_chats_with_text_and_time()
    {
        var chats = new[]
        {
            Chat(1, "A", unread: 1, hoursAgo: 4, body: "four"),
            Chat(2, "B", unread: 1, hoursAgo: 1, body: "Loved “Sounds good”"),
            Chat(3, "C", unread: 1, hoursAgo: 2, body: "two"),
            Chat(4, "D", unread: 1, hoursAgo: 3, body: "three"),
            Chat(5, "E", hoursAgo: 0, body: "read"),
        };

        var rows = WidgetContent.Build(chats, true, Today).RecentUnread;

        Assert.Equal(new[] { "B", "C", "D" }, rows.Select(r => r.Name));
        Assert.Equal("❤️ to “Sounds good”", rows[0].Text);
        Assert.Equal(Today.AddHours(11).ToString("h:mm tt"), rows[0].When);
    }

    [Fact]
    public void Message_text_can_be_hidden()
    {
        var rows = WidgetContent.Build(new[] { Chat(1, "A", unread: 1, body: "secret") }, showText: false, Today).RecentUnread;

        Assert.Equal("New message", rows.Single().Text);
    }

    [Fact]
    public void Older_messages_show_a_date()
    {
        var rows = WidgetContent.Build(new[] { Chat(1, "A", unread: 1, hoursAgo: 72) }, true, Today).RecentUnread;

        Assert.Equal(Today.AddHours(-60).ToString("MMM d"), rows.Single().When);
    }

    [Fact]
    public void Initials_and_photo_are_carried_for_each_favorite()
    {
        var chat = Chat(1, "Kim Donnelly", favorite: true);

        var person = WidgetContent.Build(new[] { chat }, true, Today).Favorites.Single();

        Assert.Equal((1L, chat.Address, "Kim Donnelly", chat.Initials, (string?)null, 0), (person.ThreadId, person.Address, person.Name, person.Initials, person.PhotoUri, person.Unread));
    }

    [Fact]
    public void Nothing_unread_and_no_favorites_gives_empty_lists()
    {
        var snapshot = WidgetContent.Build(new[] { Chat(1, "A") }, true, Today);

        Assert.Equal((0, 0, 0), (snapshot.TotalUnread, snapshot.Favorites.Count, snapshot.RecentUnread.Count));
    }
}
```

- [ ] **Step 2: RED** — `dotnet test tests/ForgeLinkSms.Core.Tests --filter WidgetContentTests` → compile error.

- [ ] **Step 3: Implement**

```csharp
using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Utils;

public sealed record WidgetPerson(long ThreadId, string Address, string Name, string Initials, string? PhotoUri, int Unread);

public sealed record WidgetRow(long ThreadId, string Address, string Name, string Text, string When);

public sealed record WidgetSnapshot(int TotalUnread, IReadOnlyList<WidgetPerson> Favorites, IReadOnlyList<WidgetRow> RecentUnread);

// What the home screen widget shows, from the chats the Chats tab shows (already filtered).
public static class WidgetContent
{
    public const int MaxFavorites = 5;
    public const int MaxRecent = 3;

    public static WidgetSnapshot Build(IReadOnlyList<SmsThread> chats, bool showText, DateTime todayLocal)
    {
        var favorites = chats
            .Where(c => c.IsFavorite)
            .OrderByDescending(c => c.UnreadCount)
            .ThenByDescending(c => c.LastMessageTimestamp)
            .Take(MaxFavorites)
            .Select(c => new WidgetPerson(c.Id, c.Address, c.DisplayNameOrAddress, c.Initials, c.PhotoUri, c.UnreadCount))
            .ToList();
        var recent = chats
            .Where(c => c.UnreadCount > 0)
            .OrderByDescending(c => c.LastMessageTimestamp)
            .Take(MaxRecent)
            .Select(c => new WidgetRow(c.Id, c.Address, c.DisplayNameOrAddress, showText ? c.PreviewText : "New message", When(c.LastMessageTimestamp, todayLocal)))
            .ToList();
        return new WidgetSnapshot(chats.Sum(c => c.UnreadCount), favorites, recent);
    }

    private static string When(DateTimeOffset timestamp, DateTime todayLocal)
    {
        var local = timestamp.LocalDateTime;
        return local.Date == todayLocal.Date ? local.ToString("h:mm tt") : local.ToString("MMM d");
    }
}
```

Note: the ordering test ranks "Five unread" before "Two unread" (more unread first), and among zero-unread favorites by recency — `OrderByDescending(UnreadCount)` does both since 0-unread ties fall to the timestamp.

- [ ] **Step 4: GREEN** — same filter passes; full suite green.

---

### Task 2: Widget provider, layouts and updater

**Files:**
- Create: `src/ForgeLinkSms/Platforms/Android/Resources/layout/forgelink_widget_small.xml`, `.../layout/forgelink_widget_tall.xml`, `.../xml/forgelink_widget_info.xml`, `.../drawable/forgelink_widget_card.xml`, `.../drawable/forgelink_widget_badge.xml`, `.../values/widget_colors.xml`, `.../values-night/widget_colors.xml`, `.../values/widget_strings.xml`
- Create: `src/ForgeLinkSms/Platforms/Android/Widget/ForgeLinkWidgetProvider.cs`, `src/ForgeLinkSms/Platforms/Android/Widget/WidgetUpdater.cs`, `src/ForgeLinkSms/Platforms/Android/Widget/WidgetBitmaps.cs`

**Interfaces:**
- Consumes: `WidgetContent.Build`, `ConversationsViewModel` (transient; `LoadCommand`, `Threads`, `Lane` defaults to Conversations), `IDisplayStyleService.GetDisplaySettings().ShowWidgetText`, `IThemeService.GetAccentColor()`.
- Produces: `static void WidgetUpdater.RequestUpdate(Context context)`.

- [ ] **Step 1: Resources.**
  - `forgelink_widget_info.xml`: `minWidth="250dp" minHeight="40dp" minResizeWidth="180dp" minResizeHeight="40dp" targetCellWidth="4" targetCellHeight="1" resizeMode="horizontal|vertical" widgetCategory="home_screen" updatePeriodMillis="1800000" initialLayout="@layout/forgelink_widget_small" previewLayout="@layout/forgelink_widget_small" description="@string/widget_description"`.
  - `widget_strings.xml`: `widget_description` = "Your favorite people and unread messages".
  - `widget_colors.xml` (values / values-night): `widget_bg` `#F2FFFFFF` / `#F21E1E1E`, `widget_text` `#1E293B` / `#F1F5F9`, `widget_hint` `#64748B` / `#94A3B8`.
  - `forgelink_widget_card.xml`: `<shape>` rounded 20dp, solid `@color/widget_bg`. `forgelink_widget_badge.xml`: `<shape android:shape="oval">` solid `#2563EB` (tinted to the accent on API 31+).
  - `forgelink_widget_small.xml`: root `LinearLayout` vertical, background card, padding 10dp, id `widget_root`. Header `LinearLayout` horizontal id `widget_header`: `TextView` `widget_title` ("ForgeLink", bold, `@color/widget_text`), `TextView` `widget_total` (badge background, white, hidden when 0), spacer, `ImageView` `widget_compose` (`@android:drawable/ic_menu_edit`, 32dp). Favorites `LinearLayout` horizontal id `widget_favorites` with 5 slots `fav0`…`fav4`, each a vertical `LinearLayout` (weight 1, gravity center) holding a `FrameLayout` 44dp with `ImageView` `favN_photo` and `TextView` `favN_badge` (top|end, badge background, 11sp white), then `TextView` `favN_name` (11sp, single line, ellipsize end). `TextView` `widget_empty_favorites` ("Heart a chat to pin it here", hint color, gone by default). `TextView` `widget_error` ("Open ForgeLink", gone by default).
  - `forgelink_widget_tall.xml`: the same plus a divider and 3 rows `row0`…`row2` (horizontal `LinearLayout`, vertical text block `rowN_name` bold 13sp + `rowN_text` 12sp hint single-line, then `rowN_when` 11sp hint), and `TextView` `widget_caught_up` ("All caught up", gone by default).

- [ ] **Step 2: `WidgetBitmaps`** — `static Bitmap Circle(Context, string? photoUri, string initials, Color accent, int sizePx = 96)`: if `photoUri` loads via `ContentResolver.OpenInputStream` + `BitmapFactory.DecodeStream` with `InSampleSize` so the short side is ≥ `sizePx`, center-crop into a `sizePx` circle using `Canvas` + `BitmapShader`; otherwise draw an accent-filled circle with the initials in white, bold, centered (`Paint.MeasureText`/`FontMetrics`). Any exception → initials bitmap.

- [ ] **Step 3: `WidgetUpdater`**

```csharp
using Android.Appwidget;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Widget;
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.Utils;
using ForgeLinkSms.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using AndroidColor = Android.Graphics.Color;

namespace ForgeLinkSms.Platforms.Android.Widget;

public static class WidgetUpdater
{
    private static readonly object Gate = new();
    private static bool _running;
    private static bool _pending;

    // Bursts of texts ask for many refreshes; one rebuild runs at a time and one more follows if asked meanwhile.
    public static void RequestUpdate(Context context)
    {
        var app = context.ApplicationContext!;
        lock (Gate)
        {
            if (_running)
            {
                _pending = true;
                return;
            }
            _running = true;
        }
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    await UpdateAllAsync(app);
                }
                catch (Exception)
                {
                }
                lock (Gate)
                {
                    if (!_pending)
                    {
                        _running = false;
                        return;
                    }
                    _pending = false;
                }
            }
        });
    }

    public static async Task UpdateAllAsync(Context context)
    {
        var manager = AppWidgetManager.GetInstance(context)!;
        var ids = manager.GetAppWidgetIds(new ComponentName(context, Java.Lang.Class.FromType(typeof(ForgeLinkWidgetProvider))))!;
        if (ids.Length == 0)
        {
            return;
        }
        WidgetSnapshot? snapshot = null;
        try
        {
            var services = MauiApplication.Current.Services;
            var chats = services.GetRequiredService<ConversationsViewModel>();
            await chats.LoadCommand.ExecuteAsync(null);
            var showText = services.GetRequiredService<IDisplayStyleService>().GetDisplaySettings().ShowWidgetText;
            snapshot = WidgetContent.Build(chats.Threads.ToList(), showText, DateTime.Today);
        }
        catch (Exception)
        {
        }
        var accent = AccentColor();
        foreach (var id in ids)
        {
            var tall = (manager.GetAppWidgetOptions(id)?.GetInt(AppWidgetManager.OptionAppwidgetMinHeight) ?? 0) >= 110;
            manager.UpdateAppWidget(id, Render(context, snapshot, tall, accent));
        }
    }
    // Render(...): pick layout; set widget_title/total/compose and header tap intents; fill fav0..fav4
    // (photo bitmap via WidgetBitmaps.Circle, badge text/visibility, first name, tap intent), hide unused
    // slots, show widget_empty_favorites when none; on tall fill row0..row2 and widget_caught_up when none;
    // snapshot null → show widget_error, hide favorites/rows. Badge tint: on API 31+
    // views.SetColorStateList(badgeId, "setBackgroundTintList", ColorStateList.ValueOf(accent)).
    // Tap intents: Intent(context, typeof(MainActivity)) with NewTask|ClearTop, PutExtra("initial_route", route),
    // PendingIntent.GetActivity(context, uniqueRequestCode, intent, UpdateCurrent|Immutable) — request codes
    // must differ per tap target (e.g. 1 header, 2 compose, 100+i favorites, 200+i rows).
}
```

(Write `Render` and `AccentColor()` fully — `AccentColor` parses `IThemeService.GetAccentColor()` with `AndroidColor.ParseColor`, falling back to `#2563EB`.)

- [ ] **Step 4: Provider**

```csharp
using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.OS;

namespace ForgeLinkSms.Platforms.Android.Widget;

[BroadcastReceiver(Label = "ForgeLink", Exported = true)]
[IntentFilter(new[] { AppWidgetManager.ActionAppwidgetUpdate })]
[MetaData(AppWidgetManager.MetaDataAppwidgetProvider, Resource = "@xml/forgelink_widget_info")]
public class ForgeLinkWidgetProvider : AppWidgetProvider
{
    public override void OnUpdate(Context? context, AppWidgetManager? manager, int[]? ids)
    {
        if (context is not null)
        {
            WidgetUpdater.RequestUpdate(context);
        }
    }

    public override void OnAppWidgetOptionsChanged(Context? context, AppWidgetManager? manager, int id, Bundle? options)
    {
        if (context is not null)
        {
            WidgetUpdater.RequestUpdate(context);
        }
    }
}
```

- [ ] **Step 5: Build** — Release build succeeds; `aapt`-generated `Resource.Id.*` names compile.

---

### Task 3: Refresh hooks and the Settings switch

**Files:**
- Modify: `src/ForgeLinkSms/Platforms/Android/IncomingMessagePipeline.cs` (after `NotifyMessageReceived`), `src/ForgeLinkSms/Platforms/Android/MainActivity.cs` (`OnPause` override → `WidgetUpdater.RequestUpdate(this)`), `src/ForgeLinkSms/Platforms/Android/MarkAsReadService.cs` (after marking read/unread), `src/ForgeLinkSms/MauiProgram.cs` (subscribe `ConversationListRefresher.RefreshRequested` → `WidgetUpdater.RequestUpdate(Android.App.Application.Context)` under `#if ANDROID`), `src/ForgeLinkSms/Pages/Settings/SettingsPage.razor` (Privacy switch "Show message text in the home screen widget" bound to `ShowWidgetText`, then `RequestUpdate`)

- [ ] **Step 1:** add the hooks above; favorites/archive/trash/snooze/block in the app are covered by the `OnPause` hook plus the list refresher (the user leaves the app to see the widget).
- [ ] **Step 2:** Settings switch next to "Show link previews", saving through the same `Update(s => s.ShowWidgetText = value)` pattern, then calling a small `IWidgetRefresher` (Core interface, Android implementation calling `WidgetUpdater.RequestUpdate`, no-op elsewhere) registered in `MauiProgram`.
- [ ] **Step 3:** Release build + full Core suite.

---

### Task 4: Device verification

- [ ] Install. Ask the user to add the widget to the home screen (long-press home → Widgets → ForgeLink) — adding widgets can't be automated reliably.
- [ ] Screenshot: names, badges and total match the app's Chats tab.
- [ ] Tap a favorite → that chat opens; ✏️ → Compose; header → Chats.
- [ ] Ask the user to resize to 4×2 → unread rows appear (or "All caught up").
- [ ] Settings → turn off "Show message text…" → rows show "New message"; turn it back on.
- [ ] Open a chat with unread messages, go home → its badge clears.
- [ ] Ask the user to send the phone a text from another phone (nothing is sent from the test phone) → widget updates within seconds.
