using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Core.ViewModels;

public partial class ThreadDetailViewModel : ObservableObject
{
    // Some conversations run to thousands of MMS messages with multi-megabyte photos/videos;
    // decoding all of them just to open the thread made it take seconds, or in the worst case
    // never finish. Loading a bounded page at a time — most recent first, older pages fetched as
    // the user scrolls up — is what keeps opening any thread fast regardless of its history.
    private const int PageSize = 50;

    // Paging in more history without ever discarding anything just moves the same unbounded-size
    // problem to a different failure point: every loaded MMS attachment (as a base64 image) stays
    // in memory and gets re-sent to the WebView in full on every render. In one media-heavy thread,
    // 200 loaded messages produced a single render-batch payload over 300MB, which the JSON
    // serializer that ships it to the WebView flatly refuses to write, crashing the whole page.
    // Capping total loaded messages and trimming the newest end as older pages come in keeps that
    // payload bounded no matter how far back the user scrolls. Trimming the newest end means the
    // loaded window can end up missing the thread's actual latest messages — LoadNewerMessages is
    // the symmetric fetch-forward that catches the window back up when the user scrolls down
    // toward "now" again (see IsAtLatest).
    private const int MaxLoadedMessages = 150;

    private const int MaxSearchResults = 100;
    private const int MinSearchLength = 2;

    private readonly ISmsService _smsService;
    private readonly IMessageSchedulerService _scheduler;
    private readonly long _threadId;
    private readonly string _address;
    private readonly TimeSpan _undoSendWindow;
    private readonly IReadOnlyList<string> _participants;
    private readonly Data.IDraftRepository? _drafts;
    private PendingSend? _pendingSend;
    private readonly IReviewPromptService? _reviewPrompt;
    private readonly ConversationListRefresher? _listRefresher;

    public ObservableCollection<Models.SmsMessage> Messages { get; } = new();

    public bool IsGroup => _participants.Count > 1;

    public ObservableCollection<Models.SmsMessage> SearchResults { get; } = new();

    // The message a search result jumped to, so the page can scroll to and flash it.
    [ObservableProperty]
    private Models.SmsMessage? _highlightedMessage;

    private int _searchVersion;

    [ObservableProperty]
    private string _composeText = string.Empty;

    [ObservableProperty]
    private bool _loadFailed;

    [ObservableProperty]
    private bool _hasMoreMessages = true;

    [ObservableProperty]
    private bool _isLoadingMore;

    // Trimming from the newest end during LoadOlderMessages (see below) discards the messages
    // closest to "now" from the loaded window — false here means the window's newest message is
    // no longer the thread's actual latest message, so scrolling back down needs to fetch forward
    // again to catch up rather than just stopping at whatever trimming left behind.
    [ObservableProperty]
    private bool _isAtLatest = true;

    [ObservableProperty]
    private bool _isLoadingNewer;

    [ObservableProperty]
    private bool _isSendPending;

    [ObservableProperty]
    private Models.SmsMessage? _replyingTo;

    private bool _isLoadingMessages;
    private DateTimeOffset? _oldestLoadedTimestamp;
    private DateTimeOffset? _newestLoadedTimestamp;

    public ThreadDetailViewModel(ISmsService smsService, IMessageSchedulerService scheduler, long threadId, string address, TimeSpan? undoSendWindow = null, IReadOnlyList<string>? participants = null, Data.IDraftRepository? drafts = null, IReviewPromptService? reviewPrompt = null, ConversationListRefresher? listRefresher = null)
    {
        _listRefresher = listRefresher;
        _drafts = drafts;
        _reviewPrompt = reviewPrompt;
        _participants = participants ?? Array.Empty<string>();
        _smsService = smsService;
        _scheduler = scheduler;
        _threadId = threadId;
        _address = address;
        _undoSendWindow = undoSendWindow ?? TimeSpan.FromSeconds(5);
    }

    [RelayCommand]
    private async Task Load()
    {
        // Opening a media-heavy thread can take a moment, and it's easy to tap it again thinking
        // the first tap didn't register. Without this guard, two overlapping loads both clear and
        // repopulate the same Messages collection, which can race and leave it empty or corrupt —
        // mirrors the same guard on ConversationsViewModel.Load for the same reason.
        if (_isLoadingMessages)
        {
            return;
        }

        _isLoadingMessages = true;
        LoadFailed = false;
        if (_drafts is not null && string.IsNullOrEmpty(ComposeText) && await _drafts.GetAsync(_threadId) is { } draft)
        {
            ComposeText = draft;
        }
        Messages.Clear();
        _oldestLoadedTimestamp = null;
        _newestLoadedTimestamp = null;
        HasMoreMessages = true;
        IsAtLatest = true;
        try
        {
            var page = await _smsService.GetMessagesAsync(_threadId, beforeTimestamp: null, PageSize) ?? Array.Empty<Models.SmsMessage>();
            var ordered = page.OrderBy(m => m.Timestamp).ToList();
            foreach (var message in ordered)
            {
                Messages.Add(message);
            }

            _oldestLoadedTimestamp = ordered.Count > 0 ? ordered[0].Timestamp : null;
            _newestLoadedTimestamp = ordered.Count > 0 ? ordered[^1].Timestamp : null;
            HasMoreMessages = page.Count >= PageSize;
            Utils.ReactionAttacher.Apply(Messages);
        }
        catch (Exception)
        {
            // Reading a thread (especially one with many MMS attachments) touches several native
            // content providers; a transient failure there shouldn't take down the whole Blazor
            // circuit and force a full app reload — surface a retry affordance instead.
            LoadFailed = true;
        }
        finally
        {
            _isLoadingMessages = false;
        }
    }

    // Concurrent so each keystroke can start a search; the version check drops results from
    // an older, slower query that finishes after a newer one.
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task Search(string query)
    {
        var version = ++_searchVersion;
        var trimmed = query.Trim();
        if (trimmed.Length < MinSearchLength)
        {
            SearchResults.Clear();
            return;
        }

        var results = await _smsService.SearchMessagesAsync(_threadId, trimmed, MaxSearchResults) ?? Array.Empty<Models.SmsMessage>();
        if (version != _searchVersion)
        {
            return;
        }
        SearchResults.Clear();
        foreach (var message in results.OrderByDescending(m => m.Timestamp))
        {
            SearchResults.Add(message);
        }
    }

    // Replaces the loaded window with the history around the found message. From there the
    // normal paging takes over: scrolling up loads older, scrolling down catches back up to now.
    [RelayCommand]
    private async Task JumpTo(Models.SmsMessage target)
    {
        if (_isLoadingMessages)
        {
            return;
        }

        _isLoadingMessages = true;
        try
        {
            // GetMessagesAsync's cursor is exclusive, so nudge it just past the target to include it.
            var page = await _smsService.GetMessagesAsync(_threadId, target.Timestamp.AddMilliseconds(1), PageSize) ?? Array.Empty<Models.SmsMessage>();
            var ordered = page.OrderBy(m => m.Timestamp).ToList();
            Messages.Clear();
            foreach (var message in ordered)
            {
                Messages.Add(message);
            }
            _oldestLoadedTimestamp = ordered.Count > 0 ? ordered[0].Timestamp : null;
            _newestLoadedTimestamp = ordered.Count > 0 ? ordered[^1].Timestamp : null;
            HasMoreMessages = page.Count >= PageSize;
            IsAtLatest = false;
            HighlightedMessage = target;
            Utils.ReactionAttacher.Apply(Messages);

            // Also load what came right after, so the found message has context below it instead
            // of sitting at the very bottom, where the next scroll would trigger this same fetch
            // and push it out of view.
            if (_newestLoadedTimestamp is not null)
            {
                await AppendNewerMessagesAsync();
            }
        }
        catch (Exception)
        {
            LoadFailed = true;
        }
        finally
        {
            _isLoadingMessages = false;
        }
    }

    [RelayCommand]
    private async Task LoadOlderMessages()
    {
        // Guards mirror Load(): don't overlap two page fetches, don't fetch past the start of
        // history, and don't run before Load() has established a cursor to page from.
        if (_isLoadingMessages || IsLoadingMore || !HasMoreMessages || _oldestLoadedTimestamp is null)
        {
            return;
        }

        _isLoadingMessages = true;
        IsLoadingMore = true;
        try
        {
            var page = await _smsService.GetMessagesAsync(_threadId, _oldestLoadedTimestamp, PageSize) ?? Array.Empty<Models.SmsMessage>();
            var ordered = page.OrderBy(m => m.Timestamp).ToList();

            // Insert + RemoveAt (not Clear+rebuild): with a stable @key on the message list, this
            // touches only the elements that actually changed, keeping each render batch bounded
            // to roughly one page's worth of new content. Clearing and re-adding the full list
            // instead defeats that keyed diffing — every already-rendered message (image data
            // included) gets treated as newly added and re-sent, which is what produced a
            // render-batch payload over 160MB and crashed the page once trimming started kicking
            // in on every subsequent page load.
            for (var i = ordered.Count - 1; i >= 0; i--)
            {
                Messages.Insert(0, ordered[i]);
            }

            // Trimming here removes from the newest end (Messages.Count - 1), which is the end
            // closest to "now" — the same end the user needs to reach when scrolling back down.
            // Once this fires, the loaded window's newest message is no longer the thread's true
            // latest, so mark that explicitly rather than leaving the user permanently stuck
            // wherever trimming happened to stop (see LoadNewerMessages, which catches back up).
            if (Messages.Count > MaxLoadedMessages)
            {
                IsAtLatest = false;
            }
            while (Messages.Count > MaxLoadedMessages)
            {
                Messages.RemoveAt(Messages.Count - 1);
            }

            if (ordered.Count > 0)
            {
                _oldestLoadedTimestamp = ordered[0].Timestamp;
            }

            // _newestLoadedTimestamp must track the window's *current* newest message, not stay
            // pinned at the timestamp Load() first saw — otherwise, once trimming has kicked in,
            // LoadNewerMessages would ask the service for "anything after the thread's true latest
            // message", which is always empty, and wrongly conclude the window is already caught
            // up to now instead of asking for "anything after what's currently displayed".
            if (Messages.Count > 0)
            {
                _newestLoadedTimestamp = Messages[^1].Timestamp;
            }

            HasMoreMessages = page.Count >= PageSize;
            Utils.ReactionAttacher.Apply(Messages);
        }
        catch (Exception)
        {
            // Best-effort — leave HasMoreMessages as-is so scrolling up again retries instead of
            // silently giving up on the rest of the thread's history.
        }
        finally
        {
            IsLoadingMore = false;
            _isLoadingMessages = false;
        }
    }

    [RelayCommand]
    private async Task LoadNewerMessages()
    {
        // The downward counterpart to LoadOlderMessages: once a newest-end trim has happened
        // above, IsAtLatest is false and scrolling toward the bottom needs to actually fetch the
        // messages that trim discarded, rather than just stopping at the trimmed boundary.
        if (_isLoadingMessages || IsLoadingNewer || IsAtLatest || _newestLoadedTimestamp is null)
        {
            return;
        }

        _isLoadingMessages = true;
        IsLoadingNewer = true;
        try
        {
            await AppendNewerMessagesAsync();
        }
        catch (Exception)
        {
            // Best-effort — leave IsAtLatest as-is so scrolling down again retries.
        }
        finally
        {
            IsLoadingNewer = false;
            _isLoadingMessages = false;
        }
    }

    [RelayCommand]
    private async Task RefreshLatest()
    {
        if (_isLoadingMessages)
        {
            return;
        }

        if (_newestLoadedTimestamp is null)
        {
            await Load();
            return;
        }

        // A trimmed window isn't showing "now" anyway; scrolling down will fetch the new
        // message along with everything else trimming discarded.
        if (!IsAtLatest)
        {
            return;
        }

        _isLoadingMessages = true;
        try
        {
            await AppendNewerMessagesAsync();
        }
        catch (Exception)
        {
            // Best-effort — reopening the thread still shows it.
        }
        finally
        {
            _isLoadingMessages = false;
        }
    }

    private async Task AppendNewerMessagesAsync()
    {
        var page = await _smsService.GetNewerMessagesAsync(_threadId, _newestLoadedTimestamp!.Value, PageSize) ?? Array.Empty<Models.SmsMessage>();
        var ordered = page.OrderBy(m => m.Timestamp).ToList();

        foreach (var message in ordered)
        {
            Messages.Add(message);
        }

        // Trimming here removes from the oldest end instead — the window's top boundary just
        // moved forward in time, so a later scroll back up needs to treat that as "there's
        // more history above" again rather than assuming it already has everything back to
        // _oldestLoadedTimestamp.
        if (Messages.Count > MaxLoadedMessages)
        {
            HasMoreMessages = true;
        }
        while (Messages.Count > MaxLoadedMessages)
        {
            Messages.RemoveAt(0);
        }

        if (Messages.Count > 0)
        {
            _oldestLoadedTimestamp = Messages[0].Timestamp;
        }
        if (ordered.Count > 0)
        {
            _newestLoadedTimestamp = ordered[^1].Timestamp;
        }

        // Fewer than a full page means there's nothing newer left in the provider — the
        // window's newest message is genuinely the thread's latest again.
        IsAtLatest = page.Count < PageSize;
        Utils.ReactionAttacher.Apply(Messages);
    }

    // A second send while one is still pending must be able to start (and flush the first)
    // rather than being blocked until the first's undo window runs out.
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task Send(Models.PickedAttachment? attachment)
    {
        var text = ComposeText.Trim();
        if (string.IsNullOrEmpty(text) && attachment is null)
        {
            return;
        }

        FlushPendingSend();
        var replyingTo = ReplyingTo;
        var outgoingText = WithReplyQuote(text, replyingTo);
        ComposeText = string.Empty;
        ReplyingTo = null;

        // The page only re-renders when this handler first yields, so the undo state must be
        // set before any await — otherwise the Undo bar never shows.
        var pending = new PendingSend(attachment);
        _pendingSend = pending;
        IsSendPending = true;
        if (_drafts is not null)
        {
            await _drafts.SaveAsync(_threadId, string.Empty);
        }
        try
        {
            await Task.Delay(_undoSendWindow, pending.Cancellation.Token);
        }
        catch (TaskCanceledException)
        {
            // Undone, or flushed early by a newer send / the page closing.
        }

        if (ReferenceEquals(_pendingSend, pending))
        {
            _pendingSend = null;
            IsSendPending = false;
        }

        if (pending.Undone)
        {
            ComposeText = string.IsNullOrEmpty(ComposeText) ? text : $"{text} {ComposeText}";
            ReplyingTo ??= replyingTo;
            return;
        }

        if (IsGroup)
        {
            await _smsService.SendGroupAsync(_threadId, _participants, string.IsNullOrEmpty(outgoingText) ? null : outgoingText, attachment);
        }
        else if (attachment is not null)
        {
            await _smsService.SendMmsAsync(_threadId, _address, string.IsNullOrEmpty(outgoingText) ? null : outgoingText, attachment);
        }
        else
        {
            await _smsService.SendAsync(_address, outgoingText);
        }

        _reviewPrompt?.RecordSent();
        // After the send, not when the undo window ends: the text only exists once it's sent, and
        // this also covers leaving the chat mid-undo, where the send finishes after the page closed.
        _listRefresher?.RequestRefresh();
        await Load();
    }

    [RelayCommand]
    private async Task DeleteMessage(Models.SmsMessage message)
    {
        await _smsService.DeleteMessageAsync(message);
        Messages.Remove(message);
        SearchResults.Remove(message);
    }

    // Called when leaving the conversation, so unsent text is still there next time.
    public Task SaveDraftAsync() => _drafts?.SaveAsync(_threadId, ComposeText) ?? Task.CompletedTask;

    public Models.PickedAttachment? UndoSend()
    {
        if (_pendingSend is not { } pending)
        {
            return null;
        }

        pending.Undone = true;
        pending.Cancellation.Cancel();
        return pending.Attachment;
    }

    public void FlushPendingSend() => _pendingSend?.Cancellation.Cancel();

    [RelayCommand]
    private async Task ScheduleSend(ScheduledSend schedule)
    {
        var text = ComposeText.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var outgoingText = WithReplyQuote(text, ReplyingTo);
        if (IsGroup)
        {
            await _scheduler.ScheduleGroupAsync(_threadId, _participants, outgoingText, schedule.SendAtUtc, schedule.Repeat);
        }
        else
        {
            await _scheduler.ScheduleAsync(_address, outgoingText, schedule.SendAtUtc, schedule.Repeat);
        }
        ComposeText = string.Empty;
        ReplyingTo = null;
    }

    [RelayCommand]
    private async Task SendReaction((string Emoji, string TargetMessageBody) reaction)
    {
        var reactionText = FormatReaction(reaction.Emoji, reaction.TargetMessageBody);
        if (IsGroup)
        {
            await _smsService.SendGroupAsync(_threadId, _participants, reactionText, null);
        }
        else
        {
            await _smsService.SendAsync(_address, reactionText);
        }
        await Load();
    }

    // Plain SMS has no reaction/tapback concept, so a reaction is sent as a real new
    // message quoting the target text — the closest a recipient can see without any
    // special client support on their end.
    private static string FormatReaction(string emoji, string targetMessageBody) =>
        $"{emoji} to \"{Quote(targetMessageBody)}\"";

    // Same limitation as reactions: SMS has no reply threading, so the quote travels in the text.
    private static string WithReplyQuote(string text, Models.SmsMessage? replyingTo)
    {
        if (replyingTo is null || string.IsNullOrEmpty(replyingTo.Body))
        {
            return text;
        }

        var quote = $"Re: \"{Quote(replyingTo.Body)}\"";
        return string.IsNullOrEmpty(text) ? quote : $"{quote}\n{text}";
    }

    private static string Quote(string body)
    {
        const int maxQuoteLength = 40;
        return body.Length > maxQuoteLength ? body[..maxQuoteLength] + "…" : body;
    }

    private sealed class PendingSend(Models.PickedAttachment? attachment)
    {
        public Models.PickedAttachment? Attachment { get; } = attachment;
        public CancellationTokenSource Cancellation { get; } = new();
        public bool Undone { get; set; }
    }
}
