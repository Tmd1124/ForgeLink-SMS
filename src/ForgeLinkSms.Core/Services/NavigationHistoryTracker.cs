namespace ForgeLinkSms.Core.Services;

/// Tracks the app's own logical navigation stack, rooted at "conversations". Android's hardware
/// back button on BlazorWebView invokes the native WebView's browser-history GoBack() by default,
/// which was confirmed via live logcat capture to sometimes pop more than one entry per press —
/// landing back on the splash screen instead of one screen back. Driving back-navigation from this
/// stack instead (see MainActivity's back-press interception) bypasses that unreliable native
/// history entirely.
public class NavigationHistoryTracker
{
    private const string RootPath = "conversations";
    private readonly object _lock = new();
    private readonly List<string> _stack = new();

    /// Set once by the Blazor circuit (which owns the real NavigationManager) so native code can
    /// drive navigation without depending on Blazor types.
    public Action<string>? NavigateAction { get; set; }

    /// A page with its own dismissible in-page state (a selection, an open overlay) sets this to
    /// claim the next back press for itself — e.g. clearing a multi-select instead of leaving the
    /// screen. Checked before the route stack, and must return false when there is nothing local
    /// to dismiss, or back presses could no longer leave that screen at all.
    public Func<bool>? LocalBackHandler { get; set; }

    /// The chat list when it sits beside an open chat: its selection and sheets are closed by Back
    /// only after the chat had nothing of its own to close. A separate slot, because the two panes
    /// finish loading in either order and would otherwise overwrite each other's handler.
    public Func<bool>? PaneBackHandler { get; set; }

    public bool TryConsumeLocalBack() => (LocalBackHandler?.Invoke() ?? false) || (PaneBackHandler?.Invoke() ?? false);

    /// Set while chats are shown beside the list: picking another chat there swaps the right
    /// pane, so Back should close the chat rather than step through every chat looked at.
    public bool CollapseChatSwitches { get; set; }

    public void RecordNavigation(string relativePath)
    {
        lock (_lock)
        {
            var pathOnly = relativePath.Split('?')[0].Trim('/');
            if (pathOnly == RootPath)
            {
                _stack.Clear();
                _stack.Add(relativePath);
                return;
            }

            if (pathOnly.Length == 0 || _stack.Count == 0 || _stack[^1] == relativePath)
            {
                return;
            }

            if (CollapseChatSwitches && IsChat(_stack[^1]) && IsChat(relativePath))
            {
                _stack[^1] = relativePath;
                return;
            }

            _stack.Add(relativePath);
        }
    }

    private static bool IsChat(string relativePath) => relativePath.Split('?')[0].Trim('/') == "conversations/thread";

    public bool TryGoBack()
    {
        string target;
        lock (_lock)
        {
            if (_stack.Count <= 1)
            {
                return false;
            }

            _stack.RemoveAt(_stack.Count - 1);
            target = _stack[^1];
        }

        NavigateAction?.Invoke(target);
        return true;
    }

    public IReadOnlyList<string> Snapshot()
    {
        lock (_lock)
        {
            return _stack.ToList();
        }
    }
}
