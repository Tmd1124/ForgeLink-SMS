# Two-Pane Chats on the Unfolded Fold Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** On a wide screen (the unfolded Fold), show the chat list and the open chat side by side; the cover screen is unchanged.

**Architecture:** `MainLayout` decides one or two panes from the viewport width (reported by a `matchMedia` JS listener) and the current route, using a Core rule `TwoPaneRules`. In two-pane mode it renders `ConversationsPage` in a left pane and `@Body` (the chat) or a placeholder in the right pane, each pane a containing block so `position:fixed` UI stays inside it. `NavigationHistoryTracker` collapses chat-to-chat switches so Back closes the chat, and the list refreshes on route changes and after sends.

**Tech Stack:** .NET 10 MAUI Blazor Hybrid (Android), xUnit + Moq, plain JS in `wwwroot/js`.

**Spec:** `docs/superpowers/specs/2026-09-30-two-pane-fold-design.md`

## Global Constraints

- Two panes only when viewport width ≥ **600 CSS px** AND route is `/conversations` or `/conversations/thread` (query ignored).
- Left pane 40% of width, minimum 320 px; 1 px divider; right pane shows "Select a chat" on `/conversations`.
- Every other page renders full width, exactly as today; one-pane mode renders `@Body` exactly as today.
- Each pane is its own containing block (`transform: translateZ(0)`; `overflow: hidden` on the detail pane, `overflow-y: auto` on the list pane).
- Back / the chat's back arrow in two-pane mode returns to `/conversations`.
- No commits unless the user asks (user rule) — commit steps below are skipped; work is staged on branch `feature/two-pane-fold`.
- No comments unless the WHY is non-obvious (user rule).
- Device testing sends no texts except to the user's own number.

## Review Focus

1. Back handling with both panes mounted: the list and chat each set `HistoryTracker.LocalBackHandler`; after the chat pane closes, Back must still close the list's selection/sheets (Task 5 chains and restores handlers).
2. Switching chats A → B → Back must close the chat, not return to A (Task 2 test `Chat_switches_replace_each_other_when_collapsing_is_on`).
3. Folding/unfolding mid-chat must not lose the open chat or leave a stale pane (Task 3 derives panes from route + width on every change; device check in Task 6).
4. Infinite scroll and "scroll list to top" inside the left pane's own scroller (bubblePond.js already finds the nearest scrolling ancestor; device check in Task 6).
5. First render on a wide screen happens before JS reports the width, so the page starts one-pane and flips; it must not double-subscribe or leak (Task 3 disposes the JS listener; ConversationsPage already unsubscribes in `Dispose`).

---

### Task 1: `TwoPaneRules`

**Files:**
- Create: `src/ForgeLinkSms.Core/Utils/TwoPaneRules.cs`
- Test: `tests/ForgeLinkSms.Core.Tests/Utils/TwoPaneRulesTests.cs`

**Interfaces:**
- Produces: `TwoPaneRules.MinWidthPx` (`const double = 600`), `TwoPaneRules.ShowsTwoPanes(double widthPx, string relativeUri) : bool`, `TwoPaneRules.IsChat(string relativeUri) : bool` (true for `conversations/thread`), `TwoPaneRules.OpenThreadId(string relativeUri) : long?` (the `id` query value on a chat route).

- [ ] **Step 1: Write the failing tests**

```csharp
using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Tests.Utils;

public class TwoPaneRulesTests
{
    [Theory]
    [InlineData(599, "conversations", false)]
    [InlineData(600, "conversations", true)]
    [InlineData(832, "conversations/thread?id=4&address=555", true)]
    [InlineData(832, "/conversations/thread?id=4", true)]
    [InlineData(412, "conversations/thread?id=4", false)]
    [InlineData(832, "settings", false)]
    [InlineData(832, "conversations/media?id=4", false)]
    [InlineData(832, "compose", false)]
    [InlineData(832, "", false)]
    public void Two_panes_show_only_for_chats_on_a_wide_screen(double width, string uri, bool expected)
    {
        Assert.Equal(expected, TwoPaneRules.ShowsTwoPanes(width, uri));
    }

    [Fact]
    public void The_open_chat_is_read_from_the_route()
    {
        Assert.Equal(4, TwoPaneRules.OpenThreadId("conversations/thread?id=4&address=555"));
        Assert.Null(TwoPaneRules.OpenThreadId("conversations"));
        Assert.Null(TwoPaneRules.OpenThreadId("conversations/thread?address=555"));
        Assert.True(TwoPaneRules.IsChat("conversations/thread?id=4"));
        Assert.False(TwoPaneRules.IsChat("conversations"));
    }
}
```

- [ ] **Step 2: Run to verify RED**

Run: `dotnet test tests/ForgeLinkSms.Core.Tests --filter TwoPaneRulesTests`
Expected: compile error "The name 'TwoPaneRules' does not exist".

- [ ] **Step 3: Implement**

```csharp
namespace ForgeLinkSms.Core.Utils;

public static class TwoPaneRules
{
    public const double MinWidthPx = 600;

    public static bool ShowsTwoPanes(double widthPx, string relativeUri) =>
        widthPx >= MinWidthPx && PathOf(relativeUri) is "conversations" or "conversations/thread";

    public static bool IsChat(string relativeUri) => PathOf(relativeUri) == "conversations/thread";

    public static long? OpenThreadId(string relativeUri)
    {
        if (!IsChat(relativeUri))
        {
            return null;
        }
        var query = relativeUri.Contains('?') ? relativeUri[(relativeUri.IndexOf('?') + 1)..] : string.Empty;
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts[0] == "id" && parts.Length == 2 && long.TryParse(parts[1], out var id))
            {
                return id;
            }
        }
        return null;
    }

    private static string PathOf(string relativeUri) => relativeUri.Split('?')[0].Trim('/');
}
```

- [ ] **Step 4: Run to verify GREEN**

Run: `dotnet test tests/ForgeLinkSms.Core.Tests --filter TwoPaneRulesTests`
Expected: all pass.

- [ ] **Step 5: Commit** — skipped (user rule); `git add` the two files.

---

### Task 2: Collapse chat switches in `NavigationHistoryTracker`

**Files:**
- Modify: `src/ForgeLinkSms.Core/Services/NavigationHistoryTracker.cs`
- Test: `tests/ForgeLinkSms.Core.Tests/Services/NavigationHistoryTrackerTests.cs`

**Interfaces:**
- Produces: `NavigationHistoryTracker.CollapseChatSwitches { get; set; } : bool` — when true, recording a chat route while the top of the stack is also a chat route replaces the top instead of pushing.

- [ ] **Step 1: Write the failing tests** (append to the existing test class)

```csharp
    [Fact]
    public void Chat_switches_replace_each_other_when_collapsing_is_on()
    {
        var tracker = new NavigationHistoryTracker { CollapseChatSwitches = true };
        tracker.RecordNavigation("conversations");
        tracker.RecordNavigation("conversations/thread?id=1");
        tracker.RecordNavigation("conversations/thread?id=2");
        string? navigatedTo = null;
        tracker.NavigateAction = path => navigatedTo = path;

        Assert.True(tracker.TryGoBack());
        Assert.Equal("conversations", navigatedTo);
    }

    [Fact]
    public void Chat_switches_stack_up_when_collapsing_is_off()
    {
        var tracker = new NavigationHistoryTracker();
        tracker.RecordNavigation("conversations");
        tracker.RecordNavigation("conversations/thread?id=1");
        tracker.RecordNavigation("conversations/thread?id=2");

        Assert.Equal(3, tracker.Snapshot().Count);
    }

    [Fact]
    public void Collapsing_leaves_other_pages_on_the_stack()
    {
        var tracker = new NavigationHistoryTracker { CollapseChatSwitches = true };
        tracker.RecordNavigation("conversations");
        tracker.RecordNavigation("conversations/thread?id=1");
        tracker.RecordNavigation("conversations/media?id=1");
        tracker.RecordNavigation("conversations/thread?id=1");

        Assert.Equal(4, tracker.Snapshot().Count);
    }
```

- [ ] **Step 2: Run to verify RED**

Run: `dotnet test tests/ForgeLinkSms.Core.Tests --filter NavigationHistoryTrackerTests`
Expected: compile error "does not contain a definition for 'CollapseChatSwitches'".

- [ ] **Step 3: Implement** — add the property and the replace branch.

```csharp
    /// Set while chats are shown beside the list: picking another chat there swaps the right
    /// pane, so Back should close the chat rather than step through every chat looked at.
    public bool CollapseChatSwitches { get; set; }
```

In `RecordNavigation`, before `_stack.Add(relativePath);`:

```csharp
            if (CollapseChatSwitches && IsChat(_stack[^1]) && IsChat(relativePath))
            {
                _stack[^1] = relativePath;
                return;
            }
```

And add:

```csharp
    private static bool IsChat(string relativePath) => relativePath.Split('?')[0].Trim('/') == "conversations/thread";
```

- [ ] **Step 4: Run to verify GREEN** — same command; all pass.

- [ ] **Step 5: Commit** — skipped; `git add`.

---

### Task 3: Split in `MainLayout` with a width watcher

**Files:**
- Create: `src/ForgeLinkSms/wwwroot/js/twoPane.js`
- Modify: `src/ForgeLinkSms/wwwroot/index.html` (add `<script src="js/twoPane.js"></script>` before `_framework/blazor.webview.js`)
- Modify: `src/ForgeLinkSms/Components/Layout/MainLayout.razor`

**Interfaces:**
- Consumes: `TwoPaneRules.ShowsTwoPanes`, `TwoPaneRules.IsChat`, `NavigationHistoryTracker.CollapseChatSwitches`.
- Produces: cascading value `[CascadingParameter(Name = "TwoPane")] bool TwoPane` available to `ConversationsPage` and `ThreadDetailPage`.

- [ ] **Step 1: JS width watcher** (`wwwroot/js/twoPane.js`)

```javascript
window.forgeLinkTwoPane = {
    watch: function (dotNetRef, minWidth) {
        this.dispose();
        var query = window.matchMedia("(min-width: " + minWidth + "px)");
        this.query = query;
        this.handler = function () { dotNetRef.invokeMethodAsync("OnWidthChanged", window.innerWidth); };
        query.addEventListener("change", this.handler);
        this.handler();
    },
    dispose: function () {
        if (this.query && this.handler) {
            this.query.removeEventListener("change", this.handler);
        }
        this.query = null;
        this.handler = null;
    }
};
```

- [ ] **Step 2: `MainLayout.razor`** — replace the whole file with (keep the BOM the file already has):

```razor
@inherits LayoutComponentBase
@implements IAsyncDisposable
@inject NavigationManager Nav
@inject Microsoft.JSInterop.IJSRuntime JS
@inject ForgeLinkSms.Core.Services.NavigationHistoryTracker HistoryTracker

@if (_twoPane)
{
    <div style="display:flex;height:100vh;width:100%;">
        <div style="flex:0 0 40%;min-width:320px;height:100vh;overflow-y:auto;position:relative;transform:translateZ(0);border-right:1px solid rgba(127,127,127,0.3);">
            <CascadingValue Name="TwoPane" Value="true">
                <ForgeLinkSms.Pages.Conversations.ConversationsPage />
            </CascadingValue>
        </div>
        <div style="flex:1;min-width:0;height:100vh;overflow:hidden;position:relative;transform:translateZ(0);">
            @if (ForgeLinkSms.Core.Utils.TwoPaneRules.IsChat(Nav.ToBaseRelativePath(Nav.Uri)))
            {
                <CascadingValue Name="TwoPane" Value="true">@Body</CascadingValue>
            }
            else
            {
                <div style="height:100%;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:8px;color:#94a3b8;text-align:center;padding:24px;">
                    <div style="font-size:2.4em;">💬</div>
                    <div style="font-size:1.1em;">Select a chat</div>
                    <div style="font-size:0.9em;">Pick a conversation on the left to read and reply here.</div>
                </div>
            }
        </div>
    </div>
}
else
{
    @Body
}

@code {
    private DotNetObjectReference<MainLayout>? _ref;
    private double _widthPx;
    private bool _twoPane;

    protected override void OnInitialized()
    {
        Nav.LocationChanged += OnLocationChanged;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _ref = DotNetObjectReference.Create(this);
            await JS.InvokeVoidAsync("forgeLinkTwoPane.watch", _ref, ForgeLinkSms.Core.Utils.TwoPaneRules.MinWidthPx);
        }
    }

    [JSInvokable]
    public void OnWidthChanged(double widthPx)
    {
        _widthPx = widthPx;
        Update();
    }

    private void OnLocationChanged(object? sender, Microsoft.AspNetCore.Components.Routing.LocationChangedEventArgs e) => Update();

    private void Update()
    {
        _twoPane = ForgeLinkSms.Core.Utils.TwoPaneRules.ShowsTwoPanes(_widthPx, Nav.ToBaseRelativePath(Nav.Uri));
        HistoryTracker.CollapseChatSwitches = _twoPane;
        _ = InvokeAsync(StateHasChanged);
    }

    public async ValueTask DisposeAsync()
    {
        Nav.LocationChanged -= OnLocationChanged;
        try
        {
            await JS.InvokeVoidAsync("forgeLinkTwoPane.dispose");
        }
        catch (JSDisconnectedException)
        {
        }
        _ref?.Dispose();
    }
}
```

Note: `@using Microsoft.JSInterop` comes from `Components/_Imports.razor`.

- [ ] **Step 3: Build**

Run: `dotnet build -c Release -f net10.0-android -p:AndroidPackageFormat=apk -p:AndroidKeyStore=false` in `src/ForgeLinkSms`
Expected: `Build succeeded.`

- [ ] **Step 4: Commit** — skipped; `git add`.

---

### Task 4: List highlights the open chat and stays current

**Files:**
- Modify: `src/ForgeLinkSms/Pages/Conversations/ConversationsPage.razor`
- Modify: `src/ForgeLinkSms/Pages/Conversations/ThreadDetailPage.razor`
- Create: `src/ForgeLinkSms.Core/Services/ConversationListRefresher.cs`
- Modify: `src/ForgeLinkSms/MauiProgram.cs` (register `ConversationListRefresher` as a singleton)
- Test: `tests/ForgeLinkSms.Core.Tests/Services/ConversationListRefresherTests.cs`

**Interfaces:**
- Consumes: `TwoPane` cascading value, `TwoPaneRules.OpenThreadId`.
- Produces: `ConversationListRefresher { event Action? RefreshRequested; void RequestRefresh(); }`.

- [ ] **Step 1: Failing test**

```csharp
using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Core.Tests.Services;

public class ConversationListRefresherTests
{
    [Fact]
    public void Requesting_a_refresh_notifies_every_listener()
    {
        var refresher = new ConversationListRefresher();
        var calls = 0;
        refresher.RefreshRequested += () => calls++;
        refresher.RefreshRequested += () => calls++;

        refresher.RequestRefresh();

        Assert.Equal(2, calls);
    }
}
```

- [ ] **Step 2: RED** — `dotnet test tests/ForgeLinkSms.Core.Tests --filter ConversationListRefresherTests` → compile error.

- [ ] **Step 3: Implement**

```csharp
namespace ForgeLinkSms.Core.Services;

/// Lets the open chat ask the chat list beside it to reload (after a send, or once it's marked read).
public class ConversationListRefresher
{
    public event Action? RefreshRequested;

    public void RequestRefresh() => RefreshRequested?.Invoke();
}
```

Register in `MauiProgram.cs` next to the other singletons: `builder.Services.AddSingleton<ConversationListRefresher>();`

- [ ] **Step 4: GREEN** — same filter passes.

- [ ] **Step 5: `ConversationsPage` changes**
  - Add `@inject ForgeLinkSms.Core.Services.ConversationListRefresher ListRefresher`.
  - Add parameter: `[CascadingParameter(Name = "TwoPane")] public bool TwoPane { get; set; }`.
  - Add field `private long? _openThreadId;` set from `TwoPaneRules.OpenThreadId(Nav.ToBaseRelativePath(Nav.Uri))` in `OnInitializedAsync` and in a new `Nav.LocationChanged` handler that also reloads the list:

```csharp
    private void OnListLocationChanged(object? sender, Microsoft.AspNetCore.Components.Routing.LocationChangedEventArgs e)
    {
        _openThreadId = ForgeLinkSms.Core.Utils.TwoPaneRules.OpenThreadId(Nav.ToBaseRelativePath(e.Location));
        OnMessageReceived(0);
    }

    private void OnRefreshRequested() => OnMessageReceived(0);
```

  - Subscribe in `OnInitializedAsync` (`Nav.LocationChanged += OnListLocationChanged; ListRefresher.RefreshRequested += OnRefreshRequested;`) and unsubscribe both in `Dispose()`.
  - In `RowBackground`, after the selection check: `if (TwoPane && thread.Id == _openThreadId) { return "rgba(99,102,241,0.28)"; }`.
  - In `OpenThread`, keep `Nav.NavigateTo(...)` as is (history collapsing handles Back).

- [ ] **Step 6: `ThreadDetailPage` changes**
  - Add `@inject ForgeLinkSms.Core.Services.ConversationListRefresher ListRefresher`.
  - After `await ViewModel.LoadCommand.ExecuteAsync(null);` in initialization: `ListRefresher.RequestRefresh();` (the chat is now read, so the list's unread badge clears).
  - Subscribe to `ViewModel.PropertyChanged` and call `ListRefresher.RequestRefresh()` when `e.PropertyName == nameof(ThreadDetailViewModel.IsSendPending) && !ViewModel.IsSendPending`; also call it after each `SendCommand.ExecuteAsync(...)` returns in `SendOrSchedule` and the voice-note send. Unsubscribe in `Dispose`.

- [ ] **Step 7: Build + full Core suite**

Run: `dotnet test tests/ForgeLinkSms.Core.Tests` then the Release build.
Expected: all pass; `Build succeeded.`

- [ ] **Step 8: Commit** — skipped; `git add`.

---

### Task 5: Back handlers with both panes mounted

**Files:**
- Modify: `src/ForgeLinkSms/Pages/Conversations/ThreadDetailPage.razor` (init at the `HistoryTracker.LocalBackHandler = TryCloseOverlayForBack;` line; dispose at the `if (HistoryTracker.LocalBackHandler == TryCloseOverlayForBack)` block)

**Interfaces:**
- Consumes: `NavigationHistoryTracker.LocalBackHandler` (`Func<bool>?`).

- [ ] **Step 1: Chain and restore** — in init:

```csharp
        _outerBackHandler = HistoryTracker.LocalBackHandler;
        HistoryTracker.LocalBackHandler = ChatThenListBack;
```

with

```csharp
    private Func<bool>? _outerBackHandler;

    // In two-pane mode the list beside the chat has its own selection and sheets to close.
    private bool ChatThenListBack() => TryCloseOverlayForBack() || (_outerBackHandler?.Invoke() ?? false);
```

and in dispose replace the existing check with:

```csharp
        if (HistoryTracker.LocalBackHandler == ChatThenListBack)
        {
            HistoryTracker.LocalBackHandler = _outerBackHandler;
        }
```

- [ ] **Step 2: Build** — Release build succeeds.

- [ ] **Step 3: Commit** — skipped; `git add`.

---

### Task 6: Device verification (needs the user to unfold the phone)

- [ ] Install: `adb install -r bin/Release/net10.0-android/com.forgelink.sms-Signed.apk`.
- [ ] Cover screen (folded): list and chats behave exactly as before (open a chat, Back).
- [ ] Ask the user to unfold. Check: list left + "Select a chat" right; open a chat → right pane; the open chat is highlighted; switch chats; Back → "Select a chat"; Back again leaves the app normally.
- [ ] Long-press a chat in the list → selection bar and sheets stay inside the left pane; Back clears the selection even with a chat open.
- [ ] In the chat: attachment drawer, reaction sheet and image viewer stay inside the right pane; compose bar sits at the bottom of the right pane.
- [ ] Scroll the list far enough to page in more chats.
- [ ] Settings from the menu opens full width; Back returns to the split.
- [ ] Ask the user to fold with a chat open → chat full screen on the cover; unfold → split returns.
- [ ] Run the full Core suite once more and record results.
