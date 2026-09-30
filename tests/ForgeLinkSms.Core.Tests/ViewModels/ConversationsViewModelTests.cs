using Moq;
using ForgeLinkSms.Core.Data;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.ViewModels;

namespace ForgeLinkSms.Core.Tests.ViewModels;

public class ConversationsViewModelTests
{
    // hasOutgoing defaults to true (an established conversation) so a nameless test thread lands
    // in the main list rather than the Screener unless a test says otherwise.
    private static SmsThread MakeThread(long id, string address, string? name, string lastMessage, DateTimeOffset? timestamp = null, int unreadCount = 0, bool hasOutgoing = true) => new()
    {
        HasOutgoing = hasOutgoing,
        Id = id,
        Address = address,
        DisplayName = name,
        LastMessageBody = lastMessage,
        LastMessageTimestamp = timestamp ?? DateTimeOffset.UtcNow,
        UnreadCount = unreadCount
    };

    private static Mock<ITrashRepository> MakeEmptyTrashRepository()
    {
        var repository = new Mock<ITrashRepository>();
        repository.Setup(r => r.GetTrashedThreadIdsAsync()).ReturnsAsync(new List<long>());
        return repository;
    }

    private static Mock<IContactBlockService> MakeEmptyBlockService()
    {
        var service = new Mock<IContactBlockService>();
        service.Setup(s => s.GetBlockedNumbersAsync()).ReturnsAsync(new List<string>());
        return service;
    }

    private static Mock<IFavoriteRepository> MakeEmptyFavoriteRepository()
    {
        var repository = new Mock<IFavoriteRepository>();
        repository.Setup(r => r.GetFavoriteThreadIdsAsync()).ReturnsAsync(new List<long>());
        return repository;
    }

    private static Mock<IArchiveRepository> MakeEmptyArchiveRepository()
    {
        var repository = new Mock<IArchiveRepository>();
        repository.Setup(r => r.GetArchivedThreadIdsAsync()).ReturnsAsync(new List<long>());
        return repository;
    }

    private static Mock<IFilterRepository> MakeEmptyFilterRepository()
    {
        var repository = new Mock<IFilterRepository>();
        repository.Setup(r => r.GetAllFiltersAsync()).ReturnsAsync(new List<Filter>());
        repository.Setup(r => r.GetAllAssignmentsAsync()).ReturnsAsync(new Dictionary<long, List<long>>());
        return repository;
    }

    private static ConversationsViewModel MakeViewModel(
        Mock<IThreadService> threadService,
        Mock<ITrashRepository>? trashRepository = null,
        Mock<IContactBlockService>? blockService = null,
        Mock<IFavoriteRepository>? favoriteRepository = null,
        Mock<IUndoStack>? undoStack = null,
        Mock<IMarkAsReadService>? markAsReadService = null,
        Mock<IArchiveRepository>? archiveRepository = null,
        Mock<IFilterRepository>? filterRepository = null,
        Mock<ISnoozeService>? snoozeService = null,
        Mock<IAllowedSenderRepository>? allowedSenderRepository = null,
        Mock<IDraftRepository>? draftRepository = null,
        Mock<IMuteRepository>? muteRepository = null,
        Mock<ISmsService>? smsService = null) =>
        new(
            threadService.Object,
            (trashRepository ?? MakeEmptyTrashRepository()).Object,
            (blockService ?? MakeEmptyBlockService()).Object,
            (favoriteRepository ?? MakeEmptyFavoriteRepository()).Object,
            (undoStack ?? new Mock<IUndoStack>()).Object,
            (markAsReadService ?? new Mock<IMarkAsReadService>()).Object,
            (archiveRepository ?? MakeEmptyArchiveRepository()).Object,
            (filterRepository ?? MakeEmptyFilterRepository()).Object,
            (snoozeService ?? MakeEmptySnoozeService()).Object,
            (allowedSenderRepository ?? MakeEmptyAllowedSenderRepository()).Object,
            (draftRepository ?? MakeEmptyDraftRepository()).Object,
            (muteRepository ?? MakeEmptyMuteRepository()).Object,
            (smsService ?? new Mock<ISmsService>()).Object);

    [Fact]
    public async Task ReportSpam_forwards_to_7726_then_blocks_and_trashes_the_sender_and_the_report_chat()
    {
        var spammer = MakeThread(5, "+1 404-555-0199", null, "You WON", hasOutgoing: false);
        var reportChat = MakeThread(9, "7726", null, "Thanks. Reply with the number");
        var threadService = new Mock<IThreadService>();
        threadService.SetupSequence(s => s.GetThreadsAsync())
            .ReturnsAsync(new List<SmsThread> { spammer })
            .ReturnsAsync(new List<SmsThread> { spammer, reportChat });
        var sms = new Mock<ISmsService>();
        sms.Setup(s => s.GetMessagesAsync(5, null, It.IsAny<int>())).ReturnsAsync(new List<SmsMessage>
        {
            new() { Id = 1, ThreadId = 5, Address = spammer.Address, Body = "Old promo", IsOutgoing = false, Status = SmsMessageStatus.Delivered, Timestamp = DateTimeOffset.UtcNow.AddHours(-2) },
            new() { Id = 2, ThreadId = 5, Address = spammer.Address, Body = "You WON a gift card", IsOutgoing = false, Status = SmsMessageStatus.Delivered, Timestamp = DateTimeOffset.UtcNow }
        });
        var sent = new List<(string To, string Body)>();
        sms.Setup(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>())).Callback<string, string>((to, body) => sent.Add((to, body))).Returns(Task.CompletedTask);
        var block = MakeEmptyBlockService();
        var trash = MakeEmptyTrashRepository();
        var viewModel = MakeViewModel(threadService, trashRepository: trash, blockService: block, smsService: sms);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.ReportSpamCommand.ExecuteAsync(spammer);

        Assert.Equal(new[] { ("7726", "You WON a gift card"), ("7726", "+1 404-555-0199") }, sent);
        block.Verify(b => b.BlockAsync("4045550199"), Times.Once);
        trash.Verify(t => t.TrashThreadAsync(5), Times.Once);
        trash.Verify(t => t.TrashThreadAsync(9), Times.Once);
    }

    private static Mock<IMuteRepository> MakeEmptyMuteRepository()
    {
        var repository = new Mock<IMuteRepository>();
        repository.Setup(r => r.GetMutedThreadIdsAsync(It.IsAny<DateTimeOffset>())).ReturnsAsync(new HashSet<long>());
        return repository;
    }

    private static Mock<IDraftRepository> MakeEmptyDraftRepository()
    {
        var repository = new Mock<IDraftRepository>();
        repository.Setup(r => r.GetAllAsync()).ReturnsAsync(new Dictionary<long, string>());
        return repository;
    }

    private static Mock<IAllowedSenderRepository> MakeEmptyAllowedSenderRepository()
    {
        var repository = new Mock<IAllowedSenderRepository>();
        repository.Setup(r => r.GetAllAsync()).ReturnsAsync(new HashSet<string>());
        return repository;
    }

    private static Mock<ISnoozeService> MakeEmptySnoozeService()
    {
        var service = new Mock<ISnoozeService>();
        service.Setup(s => s.GetSnoozedAsync()).ReturnsAsync(new Dictionary<long, DateTimeOffset>());
        return service;
    }

    [Fact]
    public async Task LoadCommand_ignores_a_second_concurrent_call_while_the_first_is_still_running()
    {
        var threadServiceTcs = new TaskCompletionSource<IReadOnlyList<SmsThread>>();
        var threadService = new Mock<IThreadService>();
        var callCount = 0;
        threadService.Setup(s => s.GetThreadsAsync()).Returns(() =>
        {
            callCount++;
            return threadServiceTcs.Task;
        });
        var viewModel = MakeViewModel(threadService);

        var firstLoad = viewModel.LoadCommand.ExecuteAsync(null);
        var secondLoad = viewModel.LoadCommand.ExecuteAsync(null);
        threadServiceTcs.SetResult(new List<SmsThread> { MakeThread(1, "5550142231", "Alice Smith", "hi") });
        await Task.WhenAll(firstLoad, secondLoad);

        Assert.Equal(1, callCount);
        Assert.Single(viewModel.Threads);
    }

    [Fact]
    public async Task ReloadCommand_during_a_load_runs_one_more_load_afterwards()
    {
        var first = new TaskCompletionSource<IReadOnlyList<SmsThread>>();
        var threadService = new Mock<IThreadService>();
        var callCount = 0;
        threadService.Setup(s => s.GetThreadsAsync()).Returns(() =>
        {
            callCount++;
            return callCount == 1
                ? first.Task
                : Task.FromResult<IReadOnlyList<SmsThread>>(new List<SmsThread> { MakeThread(1, "5550142231", "Alice Smith", "read now") });
        });
        var viewModel = MakeViewModel(threadService);

        var load = viewModel.LoadCommand.ExecuteAsync(null);
        var reload = viewModel.ReloadCommand.ExecuteAsync(null);
        var alsoReload = viewModel.ReloadCommand.ExecuteAsync(null);
        first.SetResult(new List<SmsThread> { MakeThread(1, "5550142231", "Alice Smith", "stale", unreadCount: 3) });
        await Task.WhenAll(load, reload, alsoReload);

        Assert.Equal(2, callCount);
        Assert.Equal("read now", Assert.Single(viewModel.Threads).LastMessageBody);
    }

    [Fact]
    public async Task LoadCommand_populates_Threads_from_the_service()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi"),
            MakeThread(2, "5550148890", null, "hey there")
        });
        var viewModel = MakeViewModel(threadService);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.Threads.Count);
    }

    [Fact]
    public async Task LoadCommand_excludes_trashed_threads()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi"),
            MakeThread(2, "5550148890", null, "hey there")
        });
        var trashRepository = new Mock<ITrashRepository>();
        trashRepository.Setup(r => r.GetTrashedThreadIdsAsync()).ReturnsAsync(new List<long> { 2 });
        var viewModel = MakeViewModel(threadService, trashRepository: trashRepository);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Threads);
        Assert.Equal(1, viewModel.Threads[0].Id);
    }

    [Fact]
    public async Task LoadCommand_excludes_blocked_threads()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi"),
            MakeThread(2, "5550148890", null, "hey there")
        });
        var blockService = new Mock<IContactBlockService>();
        blockService.Setup(s => s.GetBlockedNumbersAsync()).ReturnsAsync(new List<string> { "5550148890" });
        var viewModel = MakeViewModel(threadService, blockService: blockService);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Threads);
        Assert.Equal(1, viewModel.Threads[0].Id);
    }

    [Fact]
    public async Task LoadCommand_sorts_favorited_threads_to_the_top_regardless_of_recency()
    {
        var older = DateTimeOffset.UtcNow.AddDays(-5);
        var newer = DateTimeOffset.UtcNow;
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi", newer),
            MakeThread(2, "5550148890", "Bob Jones", "hey", older)
        });
        var favoriteRepository = new Mock<IFavoriteRepository>();
        favoriteRepository.Setup(r => r.GetFavoriteThreadIdsAsync()).ReturnsAsync(new List<long> { 2 });
        var viewModel = MakeViewModel(threadService, favoriteRepository: favoriteRepository);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.Threads[0].Id);
        Assert.True(viewModel.Threads[0].IsFavorite);
        Assert.Equal(1, viewModel.Threads[1].Id);
        Assert.False(viewModel.Threads[1].IsFavorite);
    }

    [Fact]
    public async Task LoadCommand_excludes_archived_threads()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi"),
            MakeThread(2, "5550148890", null, "hey there")
        });
        var archiveRepository = new Mock<IArchiveRepository>();
        archiveRepository.Setup(r => r.GetArchivedThreadIdsAsync()).ReturnsAsync(new List<long> { 2 });
        var viewModel = MakeViewModel(threadService, archiveRepository: archiveRepository);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Threads);
        Assert.Equal(1, viewModel.Threads[0].Id);
    }

    [Fact]
    public async Task ArchiveThreadCommand_archives_the_thread_and_pushes_an_undo_action()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi")
        });
        var archiveRepository = MakeEmptyArchiveRepository();
        var undoStack = new Mock<IUndoStack>();
        var viewModel = MakeViewModel(threadService, archiveRepository: archiveRepository, undoStack: undoStack);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.ArchiveThreadCommand.ExecuteAsync(1L);

        archiveRepository.Verify(r => r.ArchiveThreadAsync(1), Times.Once);
        undoStack.Verify(s => s.Push(It.IsAny<IUndoableAction>()), Times.Once);
        threadService.Verify(s => s.GetThreadsAsync(), Times.Once);
        Assert.Empty(viewModel.Threads);
    }

    [Fact]
    public async Task LoadCommand_sorts_unread_threads_below_favorites_and_above_read_threads()
    {
        var newest = DateTimeOffset.UtcNow;
        var middle = DateTimeOffset.UtcNow.AddDays(-1);
        var oldest = DateTimeOffset.UtcNow.AddDays(-10);
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi", newest, unreadCount: 0),
            MakeThread(2, "5550148890", "Bob Jones", "hey", middle, unreadCount: 3),
            MakeThread(3, "5550149999", "Carol Lee", "yo", oldest, unreadCount: 0)
        });
        var favoriteRepository = new Mock<IFavoriteRepository>();
        favoriteRepository.Setup(r => r.GetFavoriteThreadIdsAsync()).ReturnsAsync(new List<long> { 3 });
        var viewModel = MakeViewModel(threadService, favoriteRepository: favoriteRepository);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(3, viewModel.Threads[0].Id);
        Assert.Equal(2, viewModel.Threads[1].Id);
        Assert.Equal(1, viewModel.Threads[2].Id);
    }

    [Fact]
    public async Task MarkThreadReadStateCommand_marks_an_unread_thread_as_read_and_pushes_undo_action()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi", unreadCount: 2)
        });
        var markAsReadService = new Mock<IMarkAsReadService>();
        var undoStack = new Mock<IUndoStack>();
        var viewModel = MakeViewModel(threadService, markAsReadService: markAsReadService, undoStack: undoStack);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.MarkThreadReadStateCommand.ExecuteAsync(1L);

        markAsReadService.Verify(s => s.MarkThreadAsReadAsync(1), Times.Once);
        markAsReadService.Verify(s => s.MarkThreadsAsUnreadAsync(It.IsAny<IReadOnlyList<long>>()), Times.Never);
        undoStack.Verify(s => s.Push(It.IsAny<IUndoableAction>()), Times.Once);
    }

    [Fact]
    public async Task MarkThreadReadStateCommand_marks_a_read_thread_as_unread_without_pushing_undo()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi", unreadCount: 0)
        });
        var markAsReadService = new Mock<IMarkAsReadService>();
        var undoStack = new Mock<IUndoStack>();
        var viewModel = MakeViewModel(threadService, markAsReadService: markAsReadService, undoStack: undoStack);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.MarkThreadReadStateCommand.ExecuteAsync(1L);

        markAsReadService.Verify(s => s.MarkThreadsAsUnreadAsync(It.Is<IReadOnlyList<long>>(ids => ids.Count == 1 && ids[0] == 1)), Times.Once);
        markAsReadService.Verify(s => s.MarkThreadAsReadAsync(It.IsAny<long>()), Times.Never);
        undoStack.Verify(s => s.Push(It.IsAny<IUndoableAction>()), Times.Never);
    }

    [Fact]
    public async Task TrashThreadsCommand_trashes_every_thread_and_pushes_a_single_bulk_undo_action()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi"),
            MakeThread(2, "5550148890", "Bob Jones", "hey")
        });
        var trashRepository = MakeEmptyTrashRepository();
        var undoStack = new Mock<IUndoStack>();
        var viewModel = MakeViewModel(threadService, trashRepository: trashRepository, undoStack: undoStack);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.TrashThreadsCommand.ExecuteAsync(new List<long> { 1, 2 });

        trashRepository.Verify(r => r.TrashThreadAsync(1), Times.Once);
        trashRepository.Verify(r => r.TrashThreadAsync(2), Times.Once);
        undoStack.Verify(s => s.Push(It.IsAny<IUndoableAction>()), Times.Once);
    }

    [Fact]
    public async Task ArchiveThreadsCommand_archives_every_thread_and_pushes_a_single_bulk_undo_action()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi"),
            MakeThread(2, "5550148890", "Bob Jones", "hey")
        });
        var archiveRepository = MakeEmptyArchiveRepository();
        var undoStack = new Mock<IUndoStack>();
        var viewModel = MakeViewModel(threadService, archiveRepository: archiveRepository, undoStack: undoStack);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.ArchiveThreadsCommand.ExecuteAsync(new List<long> { 1, 2 });

        archiveRepository.Verify(r => r.ArchiveThreadAsync(1), Times.Once);
        archiveRepository.Verify(r => r.ArchiveThreadAsync(2), Times.Once);
        undoStack.Verify(s => s.Push(It.IsAny<IUndoableAction>()), Times.Once);
    }

    [Fact]
    public async Task TrashThreadCommand_trashes_the_thread_and_pushes_an_undo_action()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi")
        });
        var trashRepository = MakeEmptyTrashRepository();
        var undoStack = new Mock<IUndoStack>();
        var viewModel = MakeViewModel(threadService, trashRepository: trashRepository, undoStack: undoStack);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.TrashThreadCommand.ExecuteAsync(1L);

        trashRepository.Verify(r => r.TrashThreadAsync(1), Times.Once);
        undoStack.Verify(s => s.Push(It.IsAny<IUndoableAction>()), Times.Once);
        threadService.Verify(s => s.GetThreadsAsync(), Times.Once);
        Assert.Empty(viewModel.Threads);
    }

    [Fact]
    public async Task UndoCommand_undoes_the_last_action_and_reloads_threads()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi")
        });
        var undoStack = new Mock<IUndoStack>();
        undoStack.Setup(s => s.UndoAsync()).ReturnsAsync("Archived a conversation");
        var viewModel = MakeViewModel(threadService, undoStack: undoStack);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.UndoCommand.ExecuteAsync(null);

        undoStack.Verify(s => s.UndoAsync(), Times.Once);
        threadService.Verify(s => s.GetThreadsAsync(), Times.Exactly(2));
    }

    [Fact]
    public async Task MarkThreadsReadStateCommand_marks_an_all_read_selection_as_unread_in_place()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi", unreadCount: 0),
            MakeThread(2, "5550148890", "Bob Jones", "hey", unreadCount: 0)
        });
        var markAsReadService = new Mock<IMarkAsReadService>();
        var undoStack = new Mock<IUndoStack>();
        var viewModel = MakeViewModel(threadService, markAsReadService: markAsReadService, undoStack: undoStack);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.MarkThreadsReadStateCommand.ExecuteAsync(new List<long> { 1, 2 });

        markAsReadService.Verify(s => s.MarkThreadsAsUnreadAsync(It.Is<IReadOnlyList<long>>(ids => ids.Count == 2 && ids.Contains(1) && ids.Contains(2))), Times.Once);
        markAsReadService.Verify(s => s.MarkThreadsAsReadAsync(It.IsAny<IReadOnlyList<long>>()), Times.Never);
        undoStack.Verify(s => s.Push(It.IsAny<IUndoableAction>()), Times.Never);
        threadService.Verify(s => s.GetThreadsAsync(), Times.Once);
        Assert.All(viewModel.Threads, t => Assert.True(t.UnreadCount > 0));
    }

    [Fact]
    public async Task MarkThreadsReadStateCommand_marks_an_all_unread_selection_as_read_and_pushes_undo()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi", unreadCount: 2),
            MakeThread(2, "5550148890", "Bob Jones", "hey", unreadCount: 1)
        });
        var markAsReadService = new Mock<IMarkAsReadService>();
        var undoStack = new Mock<IUndoStack>();
        var viewModel = MakeViewModel(threadService, markAsReadService: markAsReadService, undoStack: undoStack);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.MarkThreadsReadStateCommand.ExecuteAsync(new List<long> { 1, 2 });

        markAsReadService.Verify(s => s.MarkThreadsAsReadAsync(It.Is<IReadOnlyList<long>>(ids => ids.Count == 2 && ids.Contains(1) && ids.Contains(2))), Times.Once);
        markAsReadService.Verify(s => s.MarkThreadsAsUnreadAsync(It.IsAny<IReadOnlyList<long>>()), Times.Never);
        undoStack.Verify(s => s.Push(It.IsAny<IUndoableAction>()), Times.Once);
        threadService.Verify(s => s.GetThreadsAsync(), Times.Once);
        Assert.All(viewModel.Threads, t => Assert.Equal(0, t.UnreadCount));
    }

    [Fact]
    public async Task MarkThreadsReadStateCommand_marks_a_mixed_selection_as_unread()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi", unreadCount: 0),
            MakeThread(2, "5550148890", "Bob Jones", "hey", unreadCount: 3)
        });
        var markAsReadService = new Mock<IMarkAsReadService>();
        var undoStack = new Mock<IUndoStack>();
        var viewModel = MakeViewModel(threadService, markAsReadService: markAsReadService, undoStack: undoStack);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.MarkThreadsReadStateCommand.ExecuteAsync(new List<long> { 1, 2 });

        markAsReadService.Verify(s => s.MarkThreadsAsUnreadAsync(It.Is<IReadOnlyList<long>>(ids => ids.Count == 2 && ids.Contains(1) && ids.Contains(2))), Times.Once);
        markAsReadService.Verify(s => s.MarkThreadsAsReadAsync(It.IsAny<IReadOnlyList<long>>()), Times.Never);
        undoStack.Verify(s => s.Push(It.IsAny<IUndoableAction>()), Times.Never);
        Assert.All(viewModel.Threads, t => Assert.True(t.UnreadCount > 0));
    }

    [Fact]
    public async Task BlockThreadCommand_blocks_the_address_pushes_an_undo_action_and_removes_it_from_Threads()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi")
        });
        var blockService = MakeEmptyBlockService();
        var undoStack = new Mock<IUndoStack>();
        var viewModel = MakeViewModel(threadService, blockService: blockService, undoStack: undoStack);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.BlockThreadCommand.ExecuteAsync("5550142231");

        blockService.Verify(s => s.BlockAsync("5550142231"), Times.Once);
        undoStack.Verify(s => s.Push(It.IsAny<IUndoableAction>()), Times.Once);
    }

    [Fact]
    public async Task FavoriteThreadCommand_favorites_a_non_favorited_thread_and_does_not_touch_undo()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi")
        });
        var favoriteRepository = MakeEmptyFavoriteRepository();
        var undoStack = new Mock<IUndoStack>();
        var viewModel = MakeViewModel(threadService, favoriteRepository: favoriteRepository, undoStack: undoStack);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.FavoriteThreadCommand.ExecuteAsync(1L);

        favoriteRepository.Verify(r => r.FavoriteThreadAsync(1), Times.Once);
        favoriteRepository.Verify(r => r.UnfavoriteThreadAsync(It.IsAny<long>()), Times.Never);
        threadService.Verify(s => s.GetThreadsAsync(), Times.Once);
        Assert.True(viewModel.Threads[0].IsFavorite);
        undoStack.Verify(s => s.Push(It.IsAny<IUndoableAction>()), Times.Never);
    }

    [Fact]
    public async Task FavoriteThreadCommand_unfavorites_an_already_favorited_thread()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi")
        });
        var favoriteRepository = new Mock<IFavoriteRepository>();
        favoriteRepository.Setup(r => r.GetFavoriteThreadIdsAsync()).ReturnsAsync(new List<long> { 1 });
        var viewModel = MakeViewModel(threadService, favoriteRepository: favoriteRepository);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.FavoriteThreadCommand.ExecuteAsync(1L);

        favoriteRepository.Verify(r => r.UnfavoriteThreadAsync(1), Times.Once);
        favoriteRepository.Verify(r => r.FavoriteThreadAsync(It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task FavoriteThreadsCommand_favorites_every_thread_when_not_all_are_already_favorited()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi"),
            MakeThread(2, "5550148890", "Bob Jones", "hey")
        });
        var favoriteRepository = new Mock<IFavoriteRepository>();
        favoriteRepository.Setup(r => r.GetFavoriteThreadIdsAsync()).ReturnsAsync(new List<long> { 1 });
        var viewModel = MakeViewModel(threadService, favoriteRepository: favoriteRepository);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.FavoriteThreadsCommand.ExecuteAsync(new List<long> { 1, 2 });

        favoriteRepository.Verify(r => r.FavoriteThreadAsync(1), Times.Once);
        favoriteRepository.Verify(r => r.FavoriteThreadAsync(2), Times.Once);
        favoriteRepository.Verify(r => r.UnfavoriteThreadAsync(It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task FavoriteThreadsCommand_unfavorites_every_thread_when_all_are_already_favorited()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi"),
            MakeThread(2, "5550148890", "Bob Jones", "hey")
        });
        var favoriteRepository = new Mock<IFavoriteRepository>();
        favoriteRepository.Setup(r => r.GetFavoriteThreadIdsAsync()).ReturnsAsync(new List<long> { 1, 2 });
        var viewModel = MakeViewModel(threadService, favoriteRepository: favoriteRepository);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.FavoriteThreadsCommand.ExecuteAsync(new List<long> { 1, 2 });

        favoriteRepository.Verify(r => r.UnfavoriteThreadAsync(1), Times.Once);
        favoriteRepository.Verify(r => r.UnfavoriteThreadAsync(2), Times.Once);
        favoriteRepository.Verify(r => r.FavoriteThreadAsync(It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task SearchText_filters_by_contact_name()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi"),
            MakeThread(2, "5550148890", "Bob Jones", "hey there")
        });
        var viewModel = MakeViewModel(threadService);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.SearchText = "alice";

        Assert.Single(viewModel.Threads);
        Assert.Equal("Alice Smith", viewModel.Threads[0].DisplayName);
    }

    [Fact]
    public async Task SearchText_filters_by_raw_address_when_no_contact_name()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi"),
            MakeThread(2, "5550148890", null, "hey there")
        });
        var viewModel = MakeViewModel(threadService);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.SearchText = "8890";

        Assert.Single(viewModel.Threads);
        Assert.Equal("5550148890", viewModel.Threads[0].Address);
    }

    [Fact]
    public async Task ShowUnreadOnly_filters_out_read_threads()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi", unreadCount: 0),
            MakeThread(2, "5550148890", "Bob Jones", "hey", unreadCount: 2)
        });
        var viewModel = MakeViewModel(threadService);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.ShowUnreadOnly = true;

        Assert.Single(viewModel.Threads);
        Assert.Equal(2, viewModel.Threads[0].Id);
    }

    [Fact]
    public async Task ShowUnreadOnly_set_back_to_false_restores_all_threads()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi", unreadCount: 0),
            MakeThread(2, "5550148890", "Bob Jones", "hey", unreadCount: 2)
        });
        var viewModel = MakeViewModel(threadService);
        await viewModel.LoadCommand.ExecuteAsync(null);
        viewModel.ShowUnreadOnly = true;

        viewModel.ShowUnreadOnly = false;

        Assert.Equal(2, viewModel.Threads.Count);
    }

    [Fact]
    public async Task SearchText_filters_by_message_content()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "let's go to the gym"),
            MakeThread(2, "5550148890", "Bob Jones", "see you tomorrow")
        });
        var viewModel = MakeViewModel(threadService);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.SearchText = "gym";

        Assert.Single(viewModel.Threads);
        Assert.Equal("Alice Smith", viewModel.Threads[0].DisplayName);
    }

    [Fact]
    public async Task LoadCommand_populates_Filters_from_the_repository()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>());
        var filterRepository = new Mock<IFilterRepository>();
        filterRepository.Setup(r => r.GetAllFiltersAsync()).ReturnsAsync(new List<Filter>
        {
            new() { Id = 1, Name = "Work", ColorHex = "#6366f1" }
        });
        filterRepository.Setup(r => r.GetAllAssignmentsAsync()).ReturnsAsync(new Dictionary<long, List<long>>());
        var viewModel = MakeViewModel(threadService, filterRepository: filterRepository);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Filters);
        Assert.Equal("Work", viewModel.Filters[0].Name);
    }

    [Fact]
    public async Task LoadCommand_populates_each_threads_FilterIds_from_assignments()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi"),
            MakeThread(2, "5550148890", "Bob Jones", "hey")
        });
        var filterRepository = new Mock<IFilterRepository>();
        filterRepository.Setup(r => r.GetAllFiltersAsync()).ReturnsAsync(new List<Filter>());
        filterRepository.Setup(r => r.GetAllAssignmentsAsync()).ReturnsAsync(new Dictionary<long, List<long>>
        {
            [1] = new List<long> { 10, 20 }
        });
        var viewModel = MakeViewModel(threadService, filterRepository: filterRepository);

        await viewModel.LoadCommand.ExecuteAsync(null);

        var thread1 = viewModel.Threads.Single(t => t.Id == 1);
        var thread2 = viewModel.Threads.Single(t => t.Id == 2);
        Assert.Equal(new List<long> { 10, 20 }, thread1.FilterIds);
        Assert.Empty(thread2.FilterIds);
    }

    [Fact]
    public async Task ToggleActiveFilter_narrows_Threads_to_matching_filter_and_toggling_again_clears_it()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi"),
            MakeThread(2, "5550148890", "Bob Jones", "hey")
        });
        var filterRepository = new Mock<IFilterRepository>();
        filterRepository.Setup(r => r.GetAllFiltersAsync()).ReturnsAsync(new List<Filter>());
        filterRepository.Setup(r => r.GetAllAssignmentsAsync()).ReturnsAsync(new Dictionary<long, List<long>>
        {
            [1] = new List<long> { 10 }
        });
        var viewModel = MakeViewModel(threadService, filterRepository: filterRepository);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.ToggleActiveFilter(10);

        Assert.Single(viewModel.Threads);
        Assert.Equal(1, viewModel.Threads[0].Id);

        viewModel.ToggleActiveFilter(10);

        Assert.Equal(2, viewModel.Threads.Count);
    }

    [Fact]
    public async Task ToggleActiveFilter_with_multiple_active_filters_matches_any_of_them()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi"),
            MakeThread(2, "5550148890", "Bob Jones", "hey"),
            MakeThread(3, "5550149999", "Carol Lee", "yo")
        });
        var filterRepository = new Mock<IFilterRepository>();
        filterRepository.Setup(r => r.GetAllFiltersAsync()).ReturnsAsync(new List<Filter>());
        filterRepository.Setup(r => r.GetAllAssignmentsAsync()).ReturnsAsync(new Dictionary<long, List<long>>
        {
            [1] = new List<long> { 10 },
            [2] = new List<long> { 20 },
            [3] = new List<long> { 30 }
        });
        var viewModel = MakeViewModel(threadService, filterRepository: filterRepository);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.ToggleActiveFilter(10);
        viewModel.ToggleActiveFilter(20);

        Assert.Equal(2, viewModel.Threads.Count);
        Assert.Contains(viewModel.Threads, t => t.Id == 1);
        Assert.Contains(viewModel.Threads, t => t.Id == 2);
    }

    [Fact]
    public async Task ActiveFilterIds_stacks_with_ShowUnreadOnly()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi", unreadCount: 0),
            MakeThread(2, "5550148890", "Bob Jones", "hey", unreadCount: 3)
        });
        var filterRepository = new Mock<IFilterRepository>();
        filterRepository.Setup(r => r.GetAllFiltersAsync()).ReturnsAsync(new List<Filter>());
        filterRepository.Setup(r => r.GetAllAssignmentsAsync()).ReturnsAsync(new Dictionary<long, List<long>>
        {
            [1] = new List<long> { 10 },
            [2] = new List<long> { 10 }
        });
        var viewModel = MakeViewModel(threadService, filterRepository: filterRepository);
        await viewModel.LoadCommand.ExecuteAsync(null);
        viewModel.ToggleActiveFilter(10);

        viewModel.ShowUnreadOnly = true;

        Assert.Single(viewModel.Threads);
        Assert.Equal(2, viewModel.Threads[0].Id);
    }

    [Fact]
    public async Task ActiveFilterIds_stacks_with_SearchText()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi"),
            MakeThread(2, "5550148890", "Bob Jones", "hey")
        });
        var filterRepository = new Mock<IFilterRepository>();
        filterRepository.Setup(r => r.GetAllFiltersAsync()).ReturnsAsync(new List<Filter>());
        filterRepository.Setup(r => r.GetAllAssignmentsAsync()).ReturnsAsync(new Dictionary<long, List<long>>
        {
            [1] = new List<long> { 10 },
            [2] = new List<long> { 10 }
        });
        var viewModel = MakeViewModel(threadService, filterRepository: filterRepository);
        await viewModel.LoadCommand.ExecuteAsync(null);
        viewModel.ToggleActiveFilter(10);

        viewModel.SearchText = "alice";

        Assert.Single(viewModel.Threads);
        Assert.Equal(1, viewModel.Threads[0].Id);
    }

    [Fact]
    public async Task LoadCommand_prunes_ActiveFilterIds_of_filters_that_no_longer_exist()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi"),
            MakeThread(2, "5550148890", "Bob Jones", "hey")
        });
        var filterRepository = new Mock<IFilterRepository>();
        filterRepository.SetupSequence(r => r.GetAllFiltersAsync())
            .ReturnsAsync(new List<Filter> { new() { Id = 10, Name = "Work", ColorHex = "#6366f1" } })
            .ReturnsAsync(new List<Filter>());
        filterRepository.Setup(r => r.GetAllAssignmentsAsync()).ReturnsAsync(new Dictionary<long, List<long>>
        {
            [1] = new List<long> { 10 }
        });
        var viewModel = MakeViewModel(threadService, filterRepository: filterRepository);
        await viewModel.LoadCommand.ExecuteAsync(null);
        viewModel.ToggleActiveFilter(10);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.DoesNotContain(10L, viewModel.ActiveFilterIds);
    }

    [Fact]
    public async Task ToggleFilterForThreadsCommand_assigns_the_filter_to_every_thread_missing_it()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi"),
            MakeThread(2, "5550148890", "Bob Jones", "hey")
        });
        var filterRepository = new Mock<IFilterRepository>();
        filterRepository.Setup(r => r.GetAllFiltersAsync()).ReturnsAsync(new List<Filter>());
        filterRepository.Setup(r => r.GetAllAssignmentsAsync()).ReturnsAsync(new Dictionary<long, List<long>>
        {
            [1] = new List<long> { 10 }
        });
        var viewModel = MakeViewModel(threadService, filterRepository: filterRepository);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.ToggleFilterForThreadsCommand.ExecuteAsync((new List<long> { 1, 2 }, 10L));

        filterRepository.Verify(r => r.AssignFilterAsync(2, 10), Times.Once);
        filterRepository.Verify(r => r.AssignFilterAsync(1, 10), Times.Never);
        filterRepository.Verify(r => r.UnassignFilterAsync(It.IsAny<long>(), It.IsAny<long>()), Times.Never);
        Assert.Contains(10L, viewModel.Threads.Single(t => t.Id == 2).FilterIds);
    }

    [Fact]
    public async Task ToggleFilterForThreadsCommand_unassigns_the_filter_when_every_selected_thread_already_has_it()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi"),
            MakeThread(2, "5550148890", "Bob Jones", "hey")
        });
        var filterRepository = new Mock<IFilterRepository>();
        filterRepository.Setup(r => r.GetAllFiltersAsync()).ReturnsAsync(new List<Filter>());
        filterRepository.Setup(r => r.GetAllAssignmentsAsync()).ReturnsAsync(new Dictionary<long, List<long>>
        {
            [1] = new List<long> { 10 },
            [2] = new List<long> { 10 }
        });
        var viewModel = MakeViewModel(threadService, filterRepository: filterRepository);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.ToggleFilterForThreadsCommand.ExecuteAsync((new List<long> { 1, 2 }, 10L));

        filterRepository.Verify(r => r.UnassignFilterAsync(1, 10), Times.Once);
        filterRepository.Verify(r => r.UnassignFilterAsync(2, 10), Times.Once);
        filterRepository.Verify(r => r.AssignFilterAsync(It.IsAny<long>(), It.IsAny<long>()), Times.Never);
        Assert.DoesNotContain(10L, viewModel.Threads.Single(t => t.Id == 1).FilterIds);
        Assert.DoesNotContain(10L, viewModel.Threads.Single(t => t.Id == 2).FilterIds);
    }

    [Fact]
    public async Task GetActiveCode_returns_the_newest_recent_code()
    {
        var now = DateTimeOffset.UtcNow;
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "72975", null, "Your verification code is 111111", now.AddMinutes(-4)),
            MakeThread(2, "Mom", "Mom", "Are you coming Sunday?", now.AddMinutes(-1)),
            MakeThread(3, "32665", null, "222222 is your login code", now.AddMinutes(-2))
        });
        var viewModel = MakeViewModel(threadService);
        await viewModel.LoadCommand.ExecuteAsync(null);

        var code = viewModel.GetActiveCode(now);

        Assert.NotNull(code);
        Assert.Equal("222222", code.Code);
        Assert.Equal(3, code.ThreadId);
        Assert.Equal("32665", code.Source);
    }

    [Fact]
    public async Task GetActiveCode_ignores_codes_older_than_ten_minutes()
    {
        var now = DateTimeOffset.UtcNow;
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "72975", null, "Your verification code is 111111", now.AddMinutes(-11))
        });
        var viewModel = MakeViewModel(threadService);
        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Null(viewModel.GetActiveCode(now));
    }

    [Fact]
    public async Task GetActiveCode_skips_a_dismissed_code()
    {
        var now = DateTimeOffset.UtcNow;
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "72975", null, "Your verification code is 111111", now.AddMinutes(-3)),
            MakeThread(2, "32665", null, "222222 is your login code", now.AddMinutes(-1))
        });
        var viewModel = MakeViewModel(threadService);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.DismissCode(viewModel.GetActiveCode(now)!);

        Assert.Equal("111111", viewModel.GetActiveCode(now)!.Code);
    }

    [Fact]
    public async Task GetActiveCode_still_finds_a_code_hidden_by_the_unread_filter()
    {
        var now = DateTimeOffset.UtcNow;
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "72975", null, "Your verification code is 111111", now.AddMinutes(-1), unreadCount: 0)
        });
        var viewModel = MakeViewModel(threadService);
        await viewModel.LoadCommand.ExecuteAsync(null);
        viewModel.ShowUnreadOnly = true;

        Assert.Equal("111111", viewModel.GetActiveCode(now)!.Code);
    }

    [Fact]
    public async Task LoadCommand_hides_snoozed_threads()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "555", "Mom", "hi"),
            MakeThread(2, "556", "Jake", "yo")
        });
        var snooze = MakeEmptySnoozeService();
        snooze.Setup(s => s.GetSnoozedAsync()).ReturnsAsync(new Dictionary<long, DateTimeOffset> { [2] = DateTimeOffset.UtcNow.AddHours(1) });
        var viewModel = MakeViewModel(threadService, snoozeService: snooze);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(new long[] { 1 }, viewModel.Threads.Select(t => t.Id));
    }

    [Fact]
    public async Task LoadCommand_wakes_expired_snoozes_before_reading_threads()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>());
        var snooze = MakeEmptySnoozeService();
        var viewModel = MakeViewModel(threadService, snoozeService: snooze);

        await viewModel.LoadCommand.ExecuteAsync(null);

        snooze.Verify(s => s.WakeExpiredAsync(It.IsAny<DateTimeOffset>()), Times.Once);
    }

    [Fact]
    public async Task SnoozeThreadCommand_snoozes_removes_from_list_and_is_undoable()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread> { MakeThread(1, "555", "Mom", "hi") });
        var snooze = MakeEmptySnoozeService();
        var undoStack = new Mock<IUndoStack>();
        var viewModel = MakeViewModel(threadService, undoStack: undoStack, snoozeService: snooze);
        await viewModel.LoadCommand.ExecuteAsync(null);
        var until = DateTimeOffset.UtcNow.AddHours(3);

        await viewModel.SnoozeThreadCommand.ExecuteAsync((1L, until));

        snooze.Verify(s => s.SnoozeAsync(1, until), Times.Once);
        Assert.Empty(viewModel.Threads);
        undoStack.Verify(u => u.Push(It.IsAny<SnoozeUndoAction>()), Times.Once);
    }

    [Fact]
    public async Task LoadCommand_moves_unknown_senders_into_the_screener()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5551112222", "Mom", "hi", hasOutgoing: false),
            MakeThread(2, "3125550147", null, "Package on hold", hasOutgoing: false),
            MakeThread(3, "5553334444", null, "Thanks for the quote", hasOutgoing: true)
        });
        var viewModel = MakeViewModel(threadService);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(new long[] { 1, 3 }, viewModel.Threads.Select(t => t.Id).OrderBy(id => id));
        Assert.Equal(1, viewModel.ScreenerCount);

        viewModel.Lane = ConversationLane.Screener;

        Assert.Equal(new long[] { 2 }, viewModel.Threads.Select(t => t.Id));
    }

    [Fact]
    public async Task LoadCommand_keeps_previously_allowed_senders_out_of_the_screener()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(2, "(312) 555-0147", null, "Your table is ready", hasOutgoing: false)
        });
        var allowed = MakeEmptyAllowedSenderRepository();
        allowed.Setup(r => r.GetAllAsync()).ReturnsAsync(new HashSet<string> { "3125550147" });
        var viewModel = MakeViewModel(threadService, allowedSenderRepository: allowed);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Threads);
        Assert.Equal(0, viewModel.ScreenerCount);
    }

    [Fact]
    public async Task AllowSenderCommand_moves_the_sender_to_the_main_list_and_is_undoable()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(2, "(312) 555-0147", null, "Your table is ready", hasOutgoing: false)
        });
        var allowed = MakeEmptyAllowedSenderRepository();
        var undoStack = new Mock<IUndoStack>();
        var viewModel = MakeViewModel(threadService, undoStack: undoStack, allowedSenderRepository: allowed);
        await viewModel.LoadCommand.ExecuteAsync(null);
        viewModel.Lane = ConversationLane.Screener;

        await viewModel.AllowSenderCommand.ExecuteAsync("(312) 555-0147");

        allowed.Verify(r => r.AllowAsync("3125550147"), Times.Once);
        undoStack.Verify(u => u.Push(It.IsAny<AllowSenderUndoAction>()), Times.Once);
        Assert.Equal(ConversationLane.Conversations, viewModel.Lane);
        Assert.Equal(new long[] { 2 }, viewModel.Threads.Select(t => t.Id));
    }

    [Fact]
    public async Task Search_finds_screened_senders_too()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(2, "3125550147", null, "Your table is ready", hasOutgoing: false)
        });
        var viewModel = MakeViewModel(threadService);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.SearchText = "table";

        Assert.Single(viewModel.Threads);
    }

    [Fact]
    public async Task LoadCommand_puts_automated_senders_in_updates_grouped_by_topic()
    {
        var now = DateTimeOffset.UtcNow;
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5551112222", "Mom", "hi", now),
            MakeThread(2, "69877", null, "UPS: out for delivery", now.AddMinutes(-5)),
            MakeThread(3, "AMAZON", null, "Your order has shipped", now.AddMinutes(-30)),
            MakeThread(4, "24273", null, "Chase: $42.18 purchase on your card", now.AddMinutes(-10))
        });
        var viewModel = MakeViewModel(threadService);
        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(new long[] { 1 }, viewModel.Threads.Select(t => t.Id));
        Assert.Equal(3, viewModel.UpdatesCount);

        viewModel.Lane = ConversationLane.Updates;

        var groups = viewModel.UpdateGroups;
        Assert.Equal(new[] { UpdateCategory.Deliveries, UpdateCategory.Banking }, groups.Select(g => g.Category));
        Assert.Equal(new long[] { 2, 3 }, groups[0].Threads.Select(t => t.Id));
    }

    [Fact]
    public async Task AllowSenderCommand_on_an_update_moves_it_to_conversations()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(2, "69877", null, "UPS: out for delivery")
        });
        var viewModel = MakeViewModel(threadService);
        await viewModel.LoadCommand.ExecuteAsync(null);
        viewModel.Lane = ConversationLane.Updates;

        await viewModel.AllowSenderCommand.ExecuteAsync("69877");

        Assert.Equal(ConversationLane.Conversations, viewModel.Lane);
        Assert.Single(viewModel.Threads);
    }

    private static List<SmsThread> FiveNamedChats() => new()
    {
        MakeThread(1, "5550000001", "Ana", "a", DateTimeOffset.UtcNow.AddMinutes(-1)),
        MakeThread(2, "5550000002", "Ben", "b", DateTimeOffset.UtcNow.AddMinutes(-2)),
        MakeThread(3, "5550000003", "Cal", "c", DateTimeOffset.UtcNow.AddMinutes(-3)),
        MakeThread(4, "5550000004", "Dee", "d", DateTimeOffset.UtcNow.AddMinutes(-4)),
        MakeThread(5, "5550000005", null, "e", DateTimeOffset.UtcNow.AddMinutes(-5))
    };

    [Fact]
    public async Task PondThreads_holds_named_chats_in_rank_order()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(FiveNamedChats());
        var favorites = MakeEmptyFavoriteRepository();
        favorites.Setup(r => r.GetFavoriteThreadIdsAsync()).ReturnsAsync(new List<long> { 4 });
        var viewModel = MakeViewModel(threadService, favoriteRepository: favorites);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(new long[] { 4, 1, 2, 3 }, viewModel.PondThreads.Select(t => t.Id));
    }

    [Fact]
    public async Task ListThreads_excludes_pond_threads()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(FiveNamedChats());
        var viewModel = MakeViewModel(threadService);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(new long[] { 5 }, viewModel.ListThreads.Select(t => t.Id));
        Assert.Empty(viewModel.PondThreads.Select(t => t.Id).Intersect(viewModel.ListThreads.Select(t => t.Id)));
    }

    [Fact]
    public async Task Pond_is_hidden_while_searching_filtering_or_off_the_chats_lane()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(FiveNamedChats());
        var viewModel = MakeViewModel(threadService);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.SearchText = "a";
        Assert.Empty(viewModel.PondThreads);
        Assert.Equal(viewModel.Threads.Select(t => t.Id), viewModel.ListThreads.Select(t => t.Id));
        viewModel.SearchText = string.Empty;

        viewModel.ShowUnreadOnly = true;
        Assert.Empty(viewModel.PondThreads);
        viewModel.ShowUnreadOnly = false;

        viewModel.Lane = ConversationLane.Updates;
        Assert.Empty(viewModel.PondThreads);
        viewModel.Lane = ConversationLane.Conversations;

        Assert.NotEmpty(viewModel.PondThreads);
    }

    [Fact]
    public async Task Cards_style_shows_every_chat_in_the_list_without_a_pond()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(FiveNamedChats());
        var viewModel = MakeViewModel(threadService);
        viewModel.Display = new DisplaySettings { Layout = ConversationDisplayStyle.Cards };

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.PondThreads);
        Assert.Empty(viewModel.GridThreads);
        Assert.Equal(new long[] { 1, 2, 3, 4, 5 }, viewModel.ListThreads.Select(t => t.Id));
    }

    [Fact]
    public async Task Bubbles_style_puts_every_chat_in_the_grid_in_rank_order()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(FiveNamedChats());
        var favorites = MakeEmptyFavoriteRepository();
        favorites.Setup(r => r.GetFavoriteThreadIdsAsync()).ReturnsAsync(new List<long> { 4 });
        var viewModel = MakeViewModel(threadService, favoriteRepository: favorites);
        viewModel.Display = new DisplaySettings { Layout = ConversationDisplayStyle.Bubbles, BubbleEveryone = true };

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(new long[] { 4, 1, 2, 3, 5 }, viewModel.GridThreads.Select(t => t.Id));
        Assert.Empty(viewModel.PondThreads);
        Assert.Empty(viewModel.ListThreads);
    }

    [Fact]
    public async Task Bubbles_style_falls_back_to_the_list_while_searching()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(FiveNamedChats());
        var viewModel = MakeViewModel(threadService);
        viewModel.Display = new DisplaySettings { Layout = ConversationDisplayStyle.Bubbles, BubbleEveryone = true };
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.SearchText = "a";

        Assert.Empty(viewModel.GridThreads);
        Assert.Equal(viewModel.Threads.Select(t => t.Id), viewModel.ListThreads.Select(t => t.Id));
    }

    [Fact]
    public async Task Changing_the_display_style_rearranges_the_loaded_chats()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(FiveNamedChats());
        var viewModel = MakeViewModel(threadService);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.Display = new DisplaySettings { Layout = ConversationDisplayStyle.Bubbles, BubbleEveryone = true };

        Assert.Equal(5, viewModel.GridThreads.Count);
    }

    [Fact]
    public async Task Bubble_checklist_decides_who_is_in_the_pond()
    {
        var chats = FiveNamedChats();
        chats[0].UnreadCount = 2;
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(chats);
        var favorites = MakeEmptyFavoriteRepository();
        favorites.Setup(r => r.GetFavoriteThreadIdsAsync()).ReturnsAsync(new List<long> { 3, 4 });
        var viewModel = MakeViewModel(threadService, favoriteRepository: favorites);
        viewModel.Display = new DisplaySettings { BubbleFavorites = true, BubbleUnread = true, BubbleRecent = false };

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(new long[] { 3, 4, 1 }, viewModel.PondThreads.Select(t => t.Id));
        Assert.Equal(new long[] { 2, 5 }, viewModel.ListThreads.Select(t => t.Id));
    }

    [Fact]
    public async Task More_bubbles_than_the_pond_holds_go_to_the_grid()
    {
        var chats = Enumerable.Range(1, 12).Select(i => MakeThread(i, $"55500000{i:00}", $"P{i}", "x", DateTimeOffset.UtcNow.AddMinutes(-i), unreadCount: 1)).ToList();
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(chats);
        var viewModel = MakeViewModel(threadService);
        viewModel.Display = new DisplaySettings { BubbleUnread = true, BubbleRecent = false, BubbleFavorites = false };

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.PondThreads);
        Assert.Equal(12, viewModel.GridThreads.Count);
        Assert.Empty(viewModel.ListThreads);
    }

    [Fact]
    public async Task Row_color_follows_the_display_settings()
    {
        var chats = FiveNamedChats();
        chats[0].UnreadCount = 1;
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(chats);
        var viewModel = MakeViewModel(threadService);
        viewModel.Display = new DisplaySettings { ColorBy = ListColorMode.None, UnreadColor = "#123456" };

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal("#123456", viewModel.RowColor(chats[0]));
        Assert.Null(viewModel.RowColor(chats[1]));
    }

    [Fact]
    public async Task Bubbles_layout_without_everyone_shows_only_the_checked_groups_and_no_list()
    {
        var chats = FiveNamedChats();
        chats[1].UnreadCount = 1;
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(chats);
        var favorites = MakeEmptyFavoriteRepository();
        favorites.Setup(r => r.GetFavoriteThreadIdsAsync()).ReturnsAsync(new List<long> { 4 });
        var viewModel = MakeViewModel(threadService, favoriteRepository: favorites);
        viewModel.Display = new DisplaySettings { Layout = ConversationDisplayStyle.Bubbles, BubbleFavorites = true, BubbleUnread = true, BubbleRecent = false };

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(new long[] { 4, 2 }, viewModel.GridThreads.Select(t => t.Id));
        Assert.Empty(viewModel.ListThreads);
        Assert.Empty(viewModel.PondThreads);
    }

    [Fact]
    public async Task FiltersInUse_lists_only_filters_with_conversations_assigned()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(FiveNamedChats());
        var filterRepository = MakeEmptyFilterRepository();
        filterRepository.Setup(r => r.GetAllFiltersAsync()).ReturnsAsync(new List<Filter>
        {
            new() { Id = 1, Name = "Family", ColorHex = "#e11d48" },
            new() { Id = 2, Name = "Work", ColorHex = "#0ea5e9" },
            new() { Id = 3, Name = "Unused", ColorHex = "#16a34a" }
        });
        filterRepository.Setup(r => r.GetAllAssignmentsAsync()).ReturnsAsync(new Dictionary<long, List<long>>
        {
            [2] = new() { 2 },
            [5] = new() { 1, 2 }
        });
        var viewModel = MakeViewModel(threadService, filterRepository: filterRepository);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(new long[] { 1, 2 }, viewModel.FiltersInUse.Select(f => f.Id));
    }

    private static Mock<IFilterRepository> OneFilterWithThread2()
    {
        var filterRepository = MakeEmptyFilterRepository();
        filterRepository.Setup(r => r.GetAllFiltersAsync()).ReturnsAsync(new List<Filter> { new() { Id = 7, Name = "Family", ColorHex = "#e11d48" } });
        filterRepository.Setup(r => r.GetAllAssignmentsAsync()).ReturnsAsync(new Dictionary<long, List<long>> { [2] = new() { 7 } });
        return filterRepository;
    }

    [Fact]
    public async Task Searching_with_a_filter_active_looks_through_every_conversation()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(FiveNamedChats());
        var viewModel = MakeViewModel(threadService, filterRepository: OneFilterWithThread2());
        await viewModel.LoadCommand.ExecuteAsync(null);
        viewModel.ToggleActiveFilter(7);
        Assert.Equal(new long[] { 2 }, viewModel.Threads.Select(t => t.Id));

        viewModel.SearchText = "n";

        Assert.True(viewModel.IsAddingToFilter);
        Assert.Contains(viewModel.Threads, t => t.Id == 1);
        Assert.False(viewModel.IsInActiveFilters(viewModel.Threads.First(t => t.Id == 1)));
        Assert.True(viewModel.IsInActiveFilters(viewModel.Threads.First(t => t.Id == 2)));
    }

    [Fact]
    public async Task AddToActiveFilters_assigns_the_conversation_to_each_active_filter_once()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(FiveNamedChats());
        var filterRepository = OneFilterWithThread2();
        var viewModel = MakeViewModel(threadService, filterRepository: filterRepository);
        await viewModel.LoadCommand.ExecuteAsync(null);
        viewModel.ToggleActiveFilter(7);
        viewModel.SearchText = "a";

        await viewModel.AddToActiveFiltersAsync(1);
        await viewModel.AddToActiveFiltersAsync(1);
        await viewModel.AddToActiveFiltersAsync(2);

        filterRepository.Verify(r => r.AssignFilterAsync(1, 7), Times.Once);
        filterRepository.Verify(r => r.AssignFilterAsync(2, 7), Times.Never);
        Assert.True(viewModel.IsInActiveFilters(viewModel.Threads.First(t => t.Id == 1)));

        viewModel.SearchText = string.Empty;
        Assert.Equal(new long[] { 1, 2 }, viewModel.Threads.Select(t => t.Id).OrderBy(id => id));
    }

    [Fact]
    public async Task SelectOnlyFilter_switches_between_filters_and_clears_on_a_second_tap()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(FiveNamedChats());
        var filterRepository = MakeEmptyFilterRepository();
        filterRepository.Setup(r => r.GetAllFiltersAsync()).ReturnsAsync(new List<Filter>
        {
            new() { Id = 1, Name = "Family", ColorHex = "#e11d48" },
            new() { Id = 2, Name = "Work", ColorHex = "#0ea5e9" }
        });
        filterRepository.Setup(r => r.GetAllAssignmentsAsync()).ReturnsAsync(new Dictionary<long, List<long>>
        {
            [1] = new() { 1 },
            [2] = new() { 2 }
        });
        var viewModel = MakeViewModel(threadService, filterRepository: filterRepository);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.SelectOnlyFilter(1);
        viewModel.SelectOnlyFilter(2);
        Assert.Equal(new long[] { 2 }, viewModel.ActiveFilterIds);
        Assert.Equal(new long[] { 2 }, viewModel.Threads.Select(t => t.Id));

        viewModel.SelectOnlyFilter(2);
        Assert.Empty(viewModel.ActiveFilterIds);
        Assert.Equal(5, viewModel.Threads.Count);
    }

    [Fact]
    public async Task SelectLane_clears_active_filters_even_for_the_current_lane()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(FiveNamedChats());
        var viewModel = MakeViewModel(threadService, filterRepository: OneFilterWithThread2());
        await viewModel.LoadCommand.ExecuteAsync(null);
        viewModel.SelectOnlyFilter(7);
        Assert.Single(viewModel.Threads);

        viewModel.SelectLane(ConversationLane.Conversations);

        Assert.Empty(viewModel.ActiveFilterIds);
        Assert.Equal(5, viewModel.Threads.Count);
    }

    [Fact]
    public async Task Pond_refills_after_a_pond_thread_is_archived()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(FiveNamedChats());
        var viewModel = MakeViewModel(threadService);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.ArchiveThreadCommand.ExecuteAsync(1L);

        Assert.Equal(new long[] { 2, 3, 4 }, viewModel.PondThreads.Select(t => t.Id));
    }

    [Fact]
    public async Task LoadCommand_marks_conversations_that_have_a_draft()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread> { MakeThread(1, "555", "Mom", "hi"), MakeThread(2, "556", "Jake", "yo") });
        var drafts = MakeEmptyDraftRepository();
        drafts.Setup(d => d.GetAllAsync()).ReturnsAsync(new Dictionary<long, string> { [2] = "Are you" });
        var viewModel = MakeViewModel(threadService, draftRepository: drafts);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Null(viewModel.Threads.Single(t => t.Id == 1).DraftText);
        Assert.Equal("Are you", viewModel.Threads.Single(t => t.Id == 2).DraftText);
    }

    [Fact]
    public async Task LoadCommand_marks_muted_conversations()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread> { MakeThread(1, "555", "Mom", "hi"), MakeThread(2, "556", "Group", "yo") });
        var mutes = MakeEmptyMuteRepository();
        mutes.Setup(m => m.GetMutedThreadIdsAsync(It.IsAny<DateTimeOffset>())).ReturnsAsync(new HashSet<long> { 2 });
        var viewModel = MakeViewModel(threadService, muteRepository: mutes);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.False(viewModel.Threads.Single(t => t.Id == 1).IsMuted);
        Assert.True(viewModel.Threads.Single(t => t.Id == 2).IsMuted);
    }

    [Fact]
    public async Task MuteThreadCommand_mutes_and_UnmuteThreadCommand_unmutes()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread> { MakeThread(1, "555", "Mom", "hi") });
        var mutes = MakeEmptyMuteRepository();
        var viewModel = MakeViewModel(threadService, muteRepository: mutes);
        await viewModel.LoadCommand.ExecuteAsync(null);
        var until = DateTimeOffset.UtcNow.AddHours(8);

        await viewModel.MuteThreadCommand.ExecuteAsync((1L, (DateTimeOffset?)until));

        mutes.Verify(m => m.MuteAsync(1, until), Times.Once);
        Assert.True(viewModel.Threads[0].IsMuted);

        await viewModel.UnmuteThreadCommand.ExecuteAsync(1L);

        mutes.Verify(m => m.UnmuteAsync(1), Times.Once);
        Assert.False(viewModel.Threads[0].IsMuted);
    }

    [Fact]
    public async Task MuteThreadCommand_can_be_undone()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread> { MakeThread(1, "555", "Mom", "hi") });
        var mutes = MakeEmptyMuteRepository();
        var undoStack = new Mock<IUndoStack>();
        IUndoableAction? pushed = null;
        undoStack.Setup(u => u.Push(It.IsAny<IUndoableAction>())).Callback<IUndoableAction>(a => pushed = a);
        var viewModel = MakeViewModel(threadService, undoStack: undoStack, muteRepository: mutes);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.MuteThreadCommand.ExecuteAsync((1L, (DateTimeOffset?)null));
        await pushed!.UndoAsync();

        mutes.Verify(m => m.UnmuteAsync(1), Times.Once);
    }

    private static SmsMessage MakeMessage(long id, long threadId, string body) => new()
    {
        Id = id,
        ThreadId = threadId,
        Address = "555",
        Body = body,
        Timestamp = DateTimeOffset.UtcNow.AddMinutes(-id),
        IsOutgoing = false,
        Status = SmsMessageStatus.Delivered
    };

    [Fact]
    public async Task SearchMessagesCommand_finds_messages_in_any_conversation()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread> { MakeThread(1, "555", "Mom", "hi"), MakeThread(2, "556", "Jake", "yo") });
        var sms = new Mock<ISmsService>();
        sms.Setup(s => s.SearchAllMessagesAsync("practice", It.IsAny<int>())).ReturnsAsync(new List<SmsMessage> { MakeMessage(10, 2, "practice at 7"), MakeMessage(11, 1, "no practice") });
        var viewModel = MakeViewModel(threadService, smsService: sms);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.SearchMessagesCommand.ExecuteAsync(" practice ");

        Assert.Equal(new long[] { 10, 11 }, viewModel.MessageResults.Select(m => m.Id));
        Assert.Equal("Jake", viewModel.ConversationNameFor(viewModel.MessageResults[0]));
    }

    [Fact]
    public async Task SearchMessagesCommand_with_a_short_query_clears_results()
    {
        var sms = new Mock<ISmsService>();
        sms.Setup(s => s.SearchAllMessagesAsync("hi", It.IsAny<int>())).ReturnsAsync(new List<SmsMessage> { MakeMessage(1, 1, "hi") });
        var viewModel = MakeViewModel(new Mock<IThreadService>(), smsService: sms);
        await viewModel.SearchMessagesCommand.ExecuteAsync("hi");

        await viewModel.SearchMessagesCommand.ExecuteAsync("h");

        Assert.Empty(viewModel.MessageResults);
    }

    [Fact]
    public async Task SearchMessagesCommand_leaves_out_trashed_blocked_and_archived_conversations()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread> { MakeThread(1, "555", "Mom", "hi") });
        var sms = new Mock<ISmsService>();
        sms.Setup(s => s.SearchAllMessagesAsync("dinner", It.IsAny<int>())).ReturnsAsync(new List<SmsMessage> { MakeMessage(10, 1, "dinner?"), MakeMessage(11, 99, "dinner is ready") });
        var viewModel = MakeViewModel(threadService, smsService: sms);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.SearchMessagesCommand.ExecuteAsync("dinner");

        Assert.Equal(new long[] { 10 }, viewModel.MessageResults.Select(m => m.Id));
    }
}
