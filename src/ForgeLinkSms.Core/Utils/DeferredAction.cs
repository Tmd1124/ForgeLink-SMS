namespace ForgeLinkSms.Core.Utils;

// Holds back a delete for a few seconds so the item can show an Undo card in its place.
// Only one item waits at a time; starting another finishes the one before it.
public sealed class DeferredAction<TKey> where TKey : notnull
{
    private readonly TimeSpan _delay;
    private readonly Func<TimeSpan, CancellationToken, Task> _wait;
    private readonly Func<Func<Task>, Task> _dispatch;
    private Func<Task>? _commit;
    private CancellationTokenSource? _cts;

    /// wait defaults to Task.Delay; dispatch runs the finished action where the page expects it
    /// (a Blazor page passes InvokeAsync).
    public DeferredAction(TimeSpan delay, Func<TimeSpan, CancellationToken, Task>? wait = null, Func<Func<Task>, Task>? dispatch = null)
    {
        _delay = delay;
        _wait = wait ?? Task.Delay;
        _dispatch = dispatch ?? (run => run());
    }

    public TKey? PendingKey { get; private set; }

    public string Label { get; private set; } = string.Empty;

    /// Raised when a waiting action finishes on its own, so the page can redraw.
    public event Action? Changed;

    public bool IsPending(TKey key) => _commit is not null && EqualityComparer<TKey>.Default.Equals(PendingKey, key);

    public async Task StartAsync(TKey key, string label, Func<Task> commit)
    {
        await CommitAsync();
        PendingKey = key;
        Label = label;
        _commit = commit;
        var cts = _cts = new CancellationTokenSource();
        _ = FinishWhenDueAsync(_wait(_delay, cts.Token), cts);
    }

    private async Task FinishWhenDueAsync(Task waiting, CancellationTokenSource cts)
    {
        try
        {
            await waiting.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (!cts.IsCancellationRequested)
        {
            await _dispatch(async () =>
            {
                // Undone, or finished early by another start, while this was on its way.
                if (_cts != cts)
                {
                    return;
                }
                await CommitAsync();
                Changed?.Invoke();
            });
        }
    }

    public async Task CommitAsync()
    {
        if (_commit is not { } commit)
        {
            return;
        }
        _cts?.Cancel();
        _commit = null;
        PendingKey = default;
        await commit();
    }

    public void Undo()
    {
        _cts?.Cancel();
        _commit = null;
        PendingKey = default;
    }
}
