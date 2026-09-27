using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForgeLinkSms.Core.Data;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.ViewModels;

public partial class ConversationsViewModel : ObservableObject
{
    private readonly IThreadService _threadService;
    private readonly ITrashRepository _trashRepository;
    private readonly IContactBlockService _blockService;
    private readonly IFavoriteRepository _favoriteRepository;
    private readonly IUndoStack _undoStack;
    private readonly IMarkAsReadService _markAsReadService;
    private readonly IArchiveRepository _archiveRepository;
    private readonly IFilterRepository _filterRepository;
    private readonly ISnoozeService _snoozeService;
    private readonly IAllowedSenderRepository _allowedSenderRepository;
    private readonly IDraftRepository _draftRepository;
    private readonly IMuteRepository _muteRepository;
    private readonly ISmsService _smsService;
    private int _messageSearchVersion;
    private const int MaxMessageResults = 50;
    private IReadOnlySet<string> _allowedSenders = new HashSet<string>();
    private IReadOnlyList<SmsThread> _allThreads = Array.Empty<SmsThread>();
    private bool _isLoadingThreads;
    private readonly HashSet<(long ThreadId, DateTimeOffset ReceivedAt)> _dismissedCodes = new();
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);

    public ObservableCollection<SmsThread> Threads { get; } = new();
    public ObservableCollection<Filter> Filters { get; } = new();
    public HashSet<long> ActiveFilterIds { get; } = new();

    /// Filters with at least one visible conversation assigned, in the user's filter order.
    public IReadOnlyList<Filter> FiltersInUse => Filters
        .Where(f => _allThreads.Any(t => t.FilterIds.Contains(f.Id)))
        .ToList();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Threads))]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Threads))]
    private bool _showUnreadOnly;

    [ObservableProperty]
    private ConversationLane _lane = ConversationLane.Conversations;

    public int ScreenerCount => _allThreads.Count(t => LaneOf(t) == ConversationLane.Screener);

    public int UpdatesCount => _allThreads.Count(t => LaneOf(t) == ConversationLane.Updates);

    // Groups the visible Updates threads by what their latest text is about, busiest-recent first.
    public IReadOnlyList<(UpdateCategory Category, IReadOnlyList<SmsThread> Threads)> UpdateGroups => Threads
        .GroupBy(t => UpdateCategorizer.Categorize(t.LastMessageBody, t.DisplayNameOrAddress))
        .OrderByDescending(g => g.Max(t => t.LastMessageTimestamp))
        .Select(g => (g.Key, (IReadOnlyList<SmsThread>)g.OrderByDescending(t => t.LastMessageTimestamp).ToList()))
        .ToList();

    public IReadOnlyList<SmsThread> PondThreads { get; private set; } = Array.Empty<SmsThread>();

    public IReadOnlyList<SmsThread> ListThreads { get; private set; } = Array.Empty<SmsThread>();

    public IReadOnlyList<SmsThread> GridThreads { get; private set; } = Array.Empty<SmsThread>();

    private DisplaySettings _display = new();

    public DisplaySettings Display
    {
        get => _display;
        set
        {
            _display = value;
            ArrangeThreads();
        }
    }

    /// The list row's tint, or null for an uncolored row.
    public string? RowColor(SmsThread thread) => DisplayRules.RowColor(thread, Display, Filters);

    private ConversationLane LaneOf(SmsThread thread) => SenderScreening.LaneFor(thread, _allowedSenders);

    public ConversationsViewModel(
        IThreadService threadService,
        ITrashRepository trashRepository,
        IContactBlockService blockService,
        IFavoriteRepository favoriteRepository,
        IUndoStack undoStack,
        IMarkAsReadService markAsReadService,
        IArchiveRepository archiveRepository,
        IFilterRepository filterRepository,
        ISnoozeService snoozeService,
        IAllowedSenderRepository allowedSenderRepository,
        IDraftRepository draftRepository,
        IMuteRepository muteRepository,
        ISmsService smsService)
    {
        _threadService = threadService;
        _trashRepository = trashRepository;
        _blockService = blockService;
        _favoriteRepository = favoriteRepository;
        _undoStack = undoStack;
        _markAsReadService = markAsReadService;
        _archiveRepository = archiveRepository;
        _filterRepository = filterRepository;
        _snoozeService = snoozeService;
        _allowedSenderRepository = allowedSenderRepository;
        _draftRepository = draftRepository;
        _muteRepository = muteRepository;
        _smsService = smsService;
    }

    /// Messages from any conversation matching the search, shown under the matching conversations.
    public ObservableCollection<SmsMessage> MessageResults { get; } = new();

    public string ConversationNameFor(SmsMessage message) =>
        _allThreads.FirstOrDefault(t => t.Id == message.ThreadId)?.DisplayNameOrAddress ?? PhoneNumberFormatter.ToDisplayFormat(message.Address);

    // Concurrent so each keystroke can start a search; the version check drops results from an
    // older, slower query that finishes after a newer one.
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SearchMessages(string query)
    {
        var version = ++_messageSearchVersion;
        var trimmed = query.Trim();
        if (trimmed.Length < 2)
        {
            MessageResults.Clear();
            return;
        }

        var results = await _smsService.SearchAllMessagesAsync(trimmed, MaxMessageResults) ?? Array.Empty<SmsMessage>();
        if (version != _messageSearchVersion)
        {
            return;
        }

        // Only conversations the list would show: not trashed, archived, blocked, or snoozed.
        var visibleThreadIds = _allThreads.Select(t => t.Id).ToHashSet();
        MessageResults.Clear();
        foreach (var message in results.Where(m => visibleThreadIds.Contains(m.ThreadId)).OrderByDescending(m => m.Timestamp))
        {
            MessageResults.Add(message);
        }
    }

    [RelayCommand]
    private async Task Load()
    {
        // ExecuteAsync is called directly from several places (initial page load, every
        // trash/archive/favorite/read action, and an app-resume hook), bypassing the
        // AsyncRelayCommand's own CanExecute gate. Without this guard, two overlapping runs
        // both read-modify-write the shared _allThreads field and Threads collection with no
        // synchronization, which can silently corrupt the visible list.
        if (_isLoadingThreads)
        {
            return;
        }

        _isLoadingThreads = true;
        try
        {
            await _snoozeService.WakeExpiredAsync(DateTimeOffset.UtcNow);
            var snoozed = await _snoozeService.GetSnoozedAsync();
            var threads = await _threadService.GetThreadsAsync();
            var trashedIds = await _trashRepository.GetTrashedThreadIdsAsync();
            var blockedNumbers = await _blockService.GetBlockedNumbersAsync();
            var favoriteIds = await _favoriteRepository.GetFavoriteThreadIdsAsync();
            var archivedIds = await _archiveRepository.GetArchivedThreadIdsAsync();
            var filters = await _filterRepository.GetAllFiltersAsync();
            var assignments = await _filterRepository.GetAllAssignmentsAsync();
            _allowedSenders = await _allowedSenderRepository.GetAllAsync();
            var drafts = await _draftRepository.GetAllAsync();
            var muted = await _muteRepository.GetMutedThreadIdsAsync(DateTimeOffset.UtcNow);

            Filters.Clear();
            foreach (var filter in filters)
            {
                Filters.Add(filter);
            }
            ActiveFilterIds.RemoveWhere(id => Filters.All(f => f.Id != id));

            _allThreads = threads
                .Where(t => !trashedIds.Contains(t.Id) && !blockedNumbers.Contains(t.Address) && !archivedIds.Contains(t.Id) && !snoozed.ContainsKey(t.Id))
                .Select(t =>
                {
                    t.IsFavorite = favoriteIds.Contains(t.Id);
                    t.FilterIds = assignments.TryGetValue(t.Id, out var ids) ? ids : new List<long>();
                    t.DraftText = drafts.TryGetValue(t.Id, out var draft) ? draft : null;
                    t.IsMuted = muted.Contains(t.Id);
                    return t;
                })
                .ToList();
            SortThreads();
            ApplyFilter();
        }
        finally
        {
            _isLoadingThreads = false;
        }
    }

    [RelayCommand]
    private async Task MarkThreadReadState(long threadId)
    {
        var thread = _allThreads.FirstOrDefault(t => t.Id == threadId);
        if (thread is null)
        {
            return;
        }

        if (thread.UnreadCount > 0)
        {
            await _markAsReadService.MarkThreadAsReadAsync(threadId);
            _undoStack.Push(new MarkAsReadUndoAction(new[] { threadId }, _markAsReadService));
            thread.UnreadCount = 0;
        }
        else
        {
            await _markAsReadService.MarkThreadsAsUnreadAsync(new[] { threadId });
            thread.UnreadCount = 1;
        }

        SortThreads();
        ApplyFilter();
    }

    [RelayCommand]
    private async Task FavoriteThread(long threadId)
    {
        var thread = _allThreads.FirstOrDefault(t => t.Id == threadId);
        if (thread is null)
        {
            return;
        }

        if (thread.IsFavorite)
        {
            await _favoriteRepository.UnfavoriteThreadAsync(threadId);
            thread.IsFavorite = false;
        }
        else
        {
            await _favoriteRepository.FavoriteThreadAsync(threadId);
            thread.IsFavorite = true;
        }

        SortThreads();
        ApplyFilter();
    }

    [RelayCommand]
    private async Task FavoriteThreads(IReadOnlyList<long> threadIds)
    {
        var threads = threadIds
            .Select(id => _allThreads.FirstOrDefault(t => t.Id == id))
            .Where(t => t is not null)
            .Cast<SmsThread>()
            .ToList();
        if (threads.Count == 0)
        {
            return;
        }

        // Mirrors the single-item toggle: if every selected thread is already a favorite,
        // the action removes them all; otherwise it favorites everything selected. No undo
        // here, matching the existing single-item Favorite toggle.
        var allFavorited = threads.All(t => t.IsFavorite);
        foreach (var thread in threads)
        {
            if (allFavorited)
            {
                await _favoriteRepository.UnfavoriteThreadAsync(thread.Id);
                thread.IsFavorite = false;
            }
            else
            {
                await _favoriteRepository.FavoriteThreadAsync(thread.Id);
                thread.IsFavorite = true;
            }
        }

        SortThreads();
        ApplyFilter();
    }

    [RelayCommand]
    private async Task TrashThread(long threadId)
    {
        await _trashRepository.TrashThreadAsync(threadId);
        _undoStack.Push(new TrashUndoAction(threadId, _trashRepository));
        RemoveThreadsFromList(new[] { threadId });
    }

    [RelayCommand]
    private async Task ArchiveThread(long threadId)
    {
        await _archiveRepository.ArchiveThreadAsync(threadId);
        _undoStack.Push(new ArchiveUndoAction(threadId, _archiveRepository));
        RemoveThreadsFromList(new[] { threadId });
    }

    [RelayCommand]
    private async Task SnoozeThread((long ThreadId, DateTimeOffset UntilUtc) args)
    {
        await _snoozeService.SnoozeAsync(args.ThreadId, args.UntilUtc);
        _undoStack.Push(new SnoozeUndoAction(args.ThreadId, _snoozeService));
        RemoveThreadsFromList(new[] { args.ThreadId });
    }

    [RelayCommand]
    private async Task TrashThreads(IReadOnlyList<long> threadIds)
    {
        var undoActions = new List<IUndoableAction>();
        foreach (var threadId in threadIds)
        {
            await _trashRepository.TrashThreadAsync(threadId);
            undoActions.Add(new TrashUndoAction(threadId, _trashRepository));
        }
        _undoStack.Push(new BulkUndoAction(undoActions, $"Trashed {threadIds.Count} conversation(s)"));
        RemoveThreadsFromList(threadIds);
    }

    [RelayCommand]
    private async Task ArchiveThreads(IReadOnlyList<long> threadIds)
    {
        var undoActions = new List<IUndoableAction>();
        foreach (var threadId in threadIds)
        {
            await _archiveRepository.ArchiveThreadAsync(threadId);
            undoActions.Add(new ArchiveUndoAction(threadId, _archiveRepository));
        }
        _undoStack.Push(new BulkUndoAction(undoActions, $"Archived {threadIds.Count} conversation(s)"));
        RemoveThreadsFromList(threadIds);
    }

    [RelayCommand]
    private async Task Undo()
    {
        // Undo restores a thread from trash/archive or flips a read flag that this ViewModel
        // doesn't otherwise track in memory (e.g. re-inserting a favorited-then-trashed thread),
        // so a full reload is worth the cost here — undo is rare, unlike the actions above.
        await _undoStack.UndoAsync();
        await Load();
    }

    [RelayCommand]
    private async Task MarkThreadsReadState(IReadOnlyList<long> threadIds)
    {
        var threads = threadIds
            .Select(id => _allThreads.FirstOrDefault(t => t.Id == id))
            .Where(t => t is not null)
            .Cast<SmsThread>()
            .ToList();
        if (threads.Count == 0)
        {
            return;
        }

        // Only a selection that's entirely unread resolves to "mark read" — an all-read
        // selection or a mixed one both resolve to "mark unread", since that's the one outcome
        // that isn't ambiguous no matter what was selected.
        var allUnread = threads.All(t => t.UnreadCount > 0);
        if (allUnread)
        {
            await _markAsReadService.MarkThreadsAsReadAsync(threadIds);
            _undoStack.Push(new MarkAsReadUndoAction(threadIds, _markAsReadService));
            foreach (var thread in threads)
            {
                thread.UnreadCount = 0;
            }
        }
        else
        {
            await _markAsReadService.MarkThreadsAsUnreadAsync(threadIds);
            foreach (var thread in threads)
            {
                thread.UnreadCount = 1;
            }
        }

        SortThreads();
        ApplyFilter();
    }

    [RelayCommand]
    private async Task MuteThread((long ThreadId, DateTimeOffset? UntilUtc) args)
    {
        await _muteRepository.MuteAsync(args.ThreadId, args.UntilUtc);
        _undoStack.Push(new MuteUndoAction(args.ThreadId, _muteRepository));
        SetMuted(args.ThreadId, true);
    }

    [RelayCommand]
    private async Task UnmuteThread(long threadId)
    {
        await _muteRepository.UnmuteAsync(threadId);
        SetMuted(threadId, false);
    }

    private void SetMuted(long threadId, bool isMuted)
    {
        if (_allThreads.FirstOrDefault(t => t.Id == threadId) is { } thread)
        {
            thread.IsMuted = isMuted;
            ApplyFilter();
        }
    }

    [RelayCommand]
    private async Task AllowSender(string address)
    {
        var normalizedAddress = PhoneNumberFormatter.ToComparableDigits(address);
        await _allowedSenderRepository.AllowAsync(normalizedAddress);
        _undoStack.Push(new AllowSenderUndoAction(normalizedAddress, _allowedSenderRepository));
        _allowedSenders = _allowedSenders.Append(normalizedAddress).ToHashSet();
        if ((Lane == ConversationLane.Screener && ScreenerCount == 0) || (Lane == ConversationLane.Updates && UpdatesCount == 0))
        {
            Lane = ConversationLane.Conversations;
        }
        ApplyFilter();
    }

    [RelayCommand]
    private async Task BlockThread(string address)
    {
        var normalizedAddress = PhoneNumberFormatter.ToComparableDigits(address);
        await _blockService.BlockAsync(normalizedAddress);
        _undoStack.Push(new BlockUndoAction(normalizedAddress, _blockService));
        _allThreads = _allThreads.Where(t => PhoneNumberFormatter.ToComparableDigits(t.Address) != normalizedAddress).ToList();
        ApplyFilter();
    }

    private void SortThreads()
    {
        _allThreads = _allThreads
            .OrderByDescending(t => t.IsFavorite)
            .ThenByDescending(t => t.UnreadCount > 0)
            .ThenByDescending(t => t.LastMessageTimestamp)
            .ToList();
    }

    private void RemoveThreadsFromList(IReadOnlyCollection<long> threadIds)
    {
        _allThreads = _allThreads.Where(t => !threadIds.Contains(t.Id)).ToList();
        ApplyFilter();
    }

    public void ToggleActiveFilter(long filterId)
    {
        if (!ActiveFilterIds.Remove(filterId))
        {
            ActiveFilterIds.Add(filterId);
        }
        ApplyFilter();
    }

    [RelayCommand]
    private async Task ToggleFilterForThreads((IReadOnlyList<long> ThreadIds, long FilterId) args)
    {
        var threads = args.ThreadIds
            .Select(id => _allThreads.FirstOrDefault(t => t.Id == id))
            .Where(t => t is not null)
            .Cast<SmsThread>()
            .ToList();
        if (threads.Count == 0)
        {
            return;
        }

        // Mirrors FavoriteThreads: if every selected thread already has this filter, the
        // action removes it from all of them; otherwise it adds it to whichever ones are
        // missing it.
        var allHaveFilter = threads.All(t => t.FilterIds.Contains(args.FilterId));
        foreach (var thread in threads)
        {
            if (allHaveFilter)
            {
                await _filterRepository.UnassignFilterAsync(thread.Id, args.FilterId);
                thread.FilterIds = thread.FilterIds.Where(id => id != args.FilterId).ToList();
            }
            else if (!thread.FilterIds.Contains(args.FilterId))
            {
                await _filterRepository.AssignFilterAsync(thread.Id, args.FilterId);
                thread.FilterIds = thread.FilterIds.Append(args.FilterId).ToList();
            }
        }

        ApplyFilter();
    }

    // Scans every loaded thread rather than the visible Threads list, so an active search or
    // unread/filter view never hides a code the user is waiting on.
    public OneTimeCode? GetActiveCode(DateTimeOffset now) => _allThreads
        .Where(t => now - t.LastMessageTimestamp <= CodeLifetime)
        .Where(t => !_dismissedCodes.Contains((t.Id, t.LastMessageTimestamp)))
        .OrderByDescending(t => t.LastMessageTimestamp)
        .Select(t => OneTimeCodeDetector.Extract(t.LastMessageBody) is { } code
            ? new OneTimeCode(code, t.DisplayNameOrAddress, t.Id, t.LastMessageTimestamp)
            : null)
        .FirstOrDefault(c => c is not null);

    public void DismissCode(OneTimeCode code) => _dismissedCodes.Add((code.ThreadId, code.ReceivedAt));

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnShowUnreadOnlyChanged(bool value) => ApplyFilter();

    partial void OnLaneChanged(ConversationLane value) => ApplyFilter();

    private void ApplyFilter()
    {
        Threads.Clear();
        var query = SearchText.Trim();
        // Search deliberately spans both lanes so a screened sender is still findable by name or text.
        var matches = string.IsNullOrEmpty(query)
            ? _allThreads.Where(t => LaneOf(t) == Lane)
            : _allThreads.Where(t =>
                t.DisplayNameOrAddress.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.LastMessageBody.Contains(query, StringComparison.OrdinalIgnoreCase));

        if (ShowUnreadOnly)
        {
            matches = matches.Where(t => t.UnreadCount > 0);
        }

        if (ActiveFilterIds.Count > 0)
        {
            matches = matches.Where(t => t.FilterIds.Any(id => ActiveFilterIds.Contains(id)));
        }

        foreach (var thread in matches)
        {
            Threads.Add(thread);
        }
        OnPropertyChanged(nameof(ScreenerCount));
        OnPropertyChanged(nameof(UpdatesCount));
        ArrangeThreads();
    }

    // Splits the visible chats between the bubble pond, the bubble grid and the list according to
    // the chosen display style. Bubbles only make sense on the plain Chats view, so searching,
    // filtering or other lanes always fall back to the list.
    private void ArrangeThreads()
    {
        var bubblesAllowed = Lane == ConversationLane.Conversations && string.IsNullOrEmpty(SearchText?.Trim())
            && !ShowUnreadOnly && ActiveFilterIds.Count == 0;
        IReadOnlyList<SmsThread> bubbles = !bubblesAllowed ? Array.Empty<SmsThread>() : Display.Layout switch
        {
            ConversationDisplayStyle.Bubbles or ConversationDisplayStyle.BubblesAndCards => DisplayRules.BubbleMembers(Threads, Display),
            _ => Array.Empty<SmsThread>()
        };
        // The floating pond has fixed slots for 3–8 people; any other count uses the grid.
        var fitsPond = Display.Layout == ConversationDisplayStyle.BubblesAndCards
            && bubbles.Count is >= PondSelector.MinBubbles and <= PondSelector.MaxBubbles;
        PondThreads = fitsPond ? bubbles : Array.Empty<SmsThread>();
        GridThreads = fitsPond ? Array.Empty<SmsThread>() : bubbles;
        var bubbleIds = PondThreads.Concat(GridThreads).Select(t => t.Id).ToHashSet();
        // The Bubbles layout has no list, so chats that aren't bubbles are simply left out.
        ListThreads = bubblesAllowed && Display.Layout == ConversationDisplayStyle.Bubbles
            ? Array.Empty<SmsThread>()
            : Threads.Where(t => !bubbleIds.Contains(t.Id)).ToList();
        OnPropertyChanged(nameof(PondThreads));
        OnPropertyChanged(nameof(GridThreads));
        OnPropertyChanged(nameof(ListThreads));
    }
}
