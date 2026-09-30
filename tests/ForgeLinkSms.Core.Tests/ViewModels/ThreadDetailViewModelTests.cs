using Moq;
using ForgeLinkSms.Core.Data;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.ViewModels;

namespace ForgeLinkSms.Core.Tests.ViewModels;

public class ThreadDetailViewModelTests
{
    private static SmsMessage MakeMessage(long id, string body, DateTimeOffset timestamp, bool isOutgoing = false) => new()
    {
        Id = id,
        ThreadId = 1,
        Address = "555",
        Body = body,
        Timestamp = timestamp,
        IsOutgoing = isOutgoing,
        Status = isOutgoing ? SmsMessageStatus.Sent : SmsMessageStatus.Delivered
    };

    [Fact]
    public async Task LoadCommand_populates_Messages_in_chronological_order()
    {
        var sms = new Mock<ISmsService>();
        sms.Setup(s => s.GetMessagesAsync(1, null, It.IsAny<int>())).ReturnsAsync(new List<SmsMessage>
        {
            MakeMessage(2, "second", DateTimeOffset.UtcNow, isOutgoing: true),
            MakeMessage(1, "first", DateTimeOffset.UtcNow.AddMinutes(-5))
        });
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "555");

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.Messages.Count);
        Assert.Equal("first", viewModel.Messages[0].Body);
        Assert.Equal("second", viewModel.Messages[1].Body);
    }

    [Fact]
    public async Task LoadCommand_sets_LoadFailed_instead_of_throwing_when_the_sms_service_throws()
    {
        var sms = new Mock<ISmsService>();
        sms.Setup(s => s.GetMessagesAsync(1, null, It.IsAny<int>())).ThrowsAsync(new InvalidOperationException("provider hiccup"));
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "555");

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.True(viewModel.LoadFailed);
        Assert.Empty(viewModel.Messages);
    }

    [Fact]
    public async Task LoadCommand_clears_LoadFailed_on_a_successful_retry()
    {
        var sms = new Mock<ISmsService>();
        sms.SetupSequence(s => s.GetMessagesAsync(1, null, It.IsAny<int>()))
            .ThrowsAsync(new InvalidOperationException("provider hiccup"))
            .ReturnsAsync(new List<SmsMessage> { MakeMessage(1, "hi", DateTimeOffset.UtcNow) });
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "555");
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.False(viewModel.LoadFailed);
        Assert.Single(viewModel.Messages);
    }

    [Fact]
    public async Task LoadCommand_sets_HasMoreMessages_false_when_the_first_page_is_short()
    {
        var sms = new Mock<ISmsService>();
        sms.Setup(s => s.GetMessagesAsync(1, null, It.IsAny<int>())).ReturnsAsync(new List<SmsMessage>
        {
            MakeMessage(1, "hi", DateTimeOffset.UtcNow)
        });
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "555");

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.False(viewModel.HasMoreMessages);
    }

    [Fact]
    public async Task LoadOlderMessagesCommand_prepends_an_older_page_using_the_oldest_loaded_timestamp_as_the_cursor()
    {
        // The ViewModel treats a full page (its internal page size, currently 50) as a signal
        // there might be more — return exactly that many so HasMoreMessages stays true and
        // LoadOlderMessagesCommand actually fetches, rather than short-circuiting.
        var newest = DateTimeOffset.UtcNow;
        var oldest = newest.AddMinutes(-50);
        var firstPage = Enumerable.Range(0, 50)
            .Select(i => MakeMessage(i + 100, $"msg-{i}", newest.AddMinutes(-i)))
            .ToList();
        firstPage[^1] = MakeMessage(1, "oldest-in-first-page", oldest);

        var sms = new Mock<ISmsService>();
        sms.Setup(s => s.GetMessagesAsync(1, null, It.IsAny<int>())).ReturnsAsync(firstPage);
        var olderMessage = MakeMessage(0, "even older", oldest.AddMinutes(-1));
        sms.Setup(s => s.GetMessagesAsync(1, oldest, It.IsAny<int>())).ReturnsAsync(new List<SmsMessage> { olderMessage });
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "555");
        await viewModel.LoadCommand.ExecuteAsync(null);
        Assert.True(viewModel.HasMoreMessages);

        await viewModel.LoadOlderMessagesCommand.ExecuteAsync(null);

        sms.Verify(s => s.GetMessagesAsync(1, oldest, It.IsAny<int>()), Times.Once);
        Assert.Equal(51, viewModel.Messages.Count);
        Assert.Equal("even older", viewModel.Messages[0].Body);
        Assert.Equal("oldest-in-first-page", viewModel.Messages[1].Body);
    }

    [Fact]
    public async Task LoadOlderMessagesCommand_trims_the_newest_end_once_total_messages_exceed_the_cap()
    {
        // Regression test: unbounded growth here is what produced a single render-batch payload
        // over 300MB in a media-heavy real thread (every loaded MMS attachment stays in memory
        // as a base64 image), which the JSON serializer shipping it to the WebView refused to
        // write, crashing the whole page. The cap (currently 150) must actually get enforced as
        // older pages come in, not just documented.
        List<SmsMessage> MakePage(int pageIndex, DateTimeOffset pageNewest) => Enumerable.Range(0, 50)
            .Select(i => MakeMessage(pageIndex * 1000 + i, $"p{pageIndex}-{i}", pageNewest.AddMinutes(-i)))
            .ToList();

        var now = DateTimeOffset.UtcNow;
        var page1 = MakePage(1, now);
        var page1Oldest = page1[^1].Timestamp;
        var page2 = MakePage(2, page1Oldest.AddMinutes(-1));
        var page2Oldest = page2[^1].Timestamp;
        var page3 = MakePage(3, page2Oldest.AddMinutes(-1));
        var page3Oldest = page3[^1].Timestamp;
        var page4 = MakePage(4, page3Oldest.AddMinutes(-1));

        var sms = new Mock<ISmsService>();
        sms.Setup(s => s.GetMessagesAsync(1, null, It.IsAny<int>())).ReturnsAsync(page1);
        sms.Setup(s => s.GetMessagesAsync(1, page1Oldest, It.IsAny<int>())).ReturnsAsync(page2);
        sms.Setup(s => s.GetMessagesAsync(1, page2Oldest, It.IsAny<int>())).ReturnsAsync(page3);
        sms.Setup(s => s.GetMessagesAsync(1, page3Oldest, It.IsAny<int>())).ReturnsAsync(page4);
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "555");
        await viewModel.LoadCommand.ExecuteAsync(null);
        await viewModel.LoadOlderMessagesCommand.ExecuteAsync(null);
        await viewModel.LoadOlderMessagesCommand.ExecuteAsync(null);
        Assert.Equal(150, viewModel.Messages.Count);

        await viewModel.LoadOlderMessagesCommand.ExecuteAsync(null);

        Assert.Equal(150, viewModel.Messages.Count);
        Assert.DoesNotContain(viewModel.Messages, m => m.Body == "p1-0");
        Assert.Contains(viewModel.Messages, m => m.Body == "p4-49");
    }

    [Fact]
    public async Task LoadNewerMessagesCommand_does_nothing_when_IsAtLatest_is_true()
    {
        var sms = new Mock<ISmsService>();
        sms.Setup(s => s.GetMessagesAsync(1, null, It.IsAny<int>())).ReturnsAsync(new List<SmsMessage>
        {
            MakeMessage(1, "hi", DateTimeOffset.UtcNow)
        });
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "555");
        await viewModel.LoadCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsAtLatest);

        await viewModel.LoadNewerMessagesCommand.ExecuteAsync(null);

        sms.Verify(s => s.GetNewerMessagesAsync(It.IsAny<long>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task LoadNewerMessagesCommand_catches_a_trimmed_window_back_up_to_latest()
    {
        // Regression test: scrolling up far enough that LoadOlderMessages trims the newest end
        // (see that test) discards the thread's actual latest messages from the loaded window.
        // Reported symptom: scrolling back down toward "now" stopped at whatever trimming had
        // left behind and never reached the real latest message, only fixable by leaving and
        // reopening the thread. LoadNewerMessages is the fetch-forward that must restore it
        // instead, trimming the *oldest* end this time so the window stays bounded either way.
        List<SmsMessage> MakePage(int pageIndex, DateTimeOffset pageNewest) => Enumerable.Range(0, 50)
            .Select(i => MakeMessage(pageIndex * 1000 + i, $"p{pageIndex}-{i}", pageNewest.AddMinutes(-i)))
            .ToList();

        var now = DateTimeOffset.UtcNow;
        var page1 = MakePage(1, now);
        var page1Oldest = page1[^1].Timestamp;
        var page2 = MakePage(2, page1Oldest.AddMinutes(-1));
        var page2Newest = page2[0].Timestamp;
        var page2Oldest = page2[^1].Timestamp;
        var page3 = MakePage(3, page2Oldest.AddMinutes(-1));
        var page3Oldest = page3[^1].Timestamp;
        var page4 = MakePage(4, page3Oldest.AddMinutes(-1));
        var catchUpPage = Enumerable.Range(0, 10)
            .Select(i => MakeMessage(9000 + i, $"newer-{i}", now.AddMinutes(i + 1)))
            .ToList();

        var sms = new Mock<ISmsService>();
        sms.Setup(s => s.GetMessagesAsync(1, null, It.IsAny<int>())).ReturnsAsync(page1);
        sms.Setup(s => s.GetMessagesAsync(1, page1Oldest, It.IsAny<int>())).ReturnsAsync(page2);
        sms.Setup(s => s.GetMessagesAsync(1, page2Oldest, It.IsAny<int>())).ReturnsAsync(page3);
        sms.Setup(s => s.GetMessagesAsync(1, page3Oldest, It.IsAny<int>())).ReturnsAsync(page4);
        // The window's newest displayed message after the trim below is page2's newest (page1
        // got trimmed away entirely) — LoadNewerMessages must resume from there, not from the
        // thread's true original latest ("now"), which trimming has already left behind.
        sms.Setup(s => s.GetNewerMessagesAsync(1, page2Newest, It.IsAny<int>())).ReturnsAsync(catchUpPage);
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "555");
        await viewModel.LoadCommand.ExecuteAsync(null);
        await viewModel.LoadOlderMessagesCommand.ExecuteAsync(null);
        await viewModel.LoadOlderMessagesCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsAtLatest);

        await viewModel.LoadOlderMessagesCommand.ExecuteAsync(null);
        Assert.False(viewModel.IsAtLatest);
        Assert.DoesNotContain(viewModel.Messages, m => m.Body == "p1-0");

        await viewModel.LoadNewerMessagesCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsAtLatest);
        Assert.True(viewModel.HasMoreMessages);
        Assert.Equal(150, viewModel.Messages.Count);
        Assert.Contains(viewModel.Messages, m => m.Body == "newer-9");
        Assert.DoesNotContain(viewModel.Messages, m => m.Body == "p4-49");
        Assert.Contains(viewModel.Messages, m => m.Body == "p4-0");
    }

    [Fact]
    public async Task RefreshLatestCommand_appends_a_newly_received_message_without_reloading()
    {
        var now = DateTimeOffset.UtcNow;
        var sms = new Mock<ISmsService>();
        sms.Setup(s => s.GetMessagesAsync(1, null, It.IsAny<int>())).ReturnsAsync(new List<SmsMessage>
        {
            MakeMessage(1, "hi", now)
        });
        sms.Setup(s => s.GetNewerMessagesAsync(1, now, It.IsAny<int>())).ReturnsAsync(new List<SmsMessage>
        {
            MakeMessage(2, "reply", now.AddSeconds(5))
        });
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "555");
        await viewModel.LoadCommand.ExecuteAsync(null);
        var first = viewModel.Messages[0];

        await viewModel.RefreshLatestCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "hi", "reply" }, viewModel.Messages.Select(m => m.Body));
        Assert.Same(first, viewModel.Messages[0]);
        Assert.True(viewModel.IsAtLatest);
        sms.Verify(s => s.GetMessagesAsync(1, null, It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task RefreshLatestCommand_loads_the_thread_when_nothing_was_loaded_yet()
    {
        var sms = new Mock<ISmsService>();
        sms.SetupSequence(s => s.GetMessagesAsync(1, null, It.IsAny<int>()))
            .ReturnsAsync(new List<SmsMessage>())
            .ReturnsAsync(new List<SmsMessage> { MakeMessage(1, "first ever", DateTimeOffset.UtcNow) });
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "555");
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.RefreshLatestCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Messages, m => m.Body == "first ever");
    }

    [Fact]
    public async Task LoadOlderMessagesCommand_does_nothing_when_HasMoreMessages_is_false()
    {
        var sms = new Mock<ISmsService>();
        sms.Setup(s => s.GetMessagesAsync(1, null, It.IsAny<int>())).ReturnsAsync(new List<SmsMessage>
        {
            MakeMessage(1, "only message", DateTimeOffset.UtcNow)
        });
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "555");
        await viewModel.LoadCommand.ExecuteAsync(null);
        Assert.False(viewModel.HasMoreMessages);

        await viewModel.LoadOlderMessagesCommand.ExecuteAsync(null);

        sms.Verify(s => s.GetMessagesAsync(1, It.IsAny<DateTimeOffset?>(), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task SendCommand_does_nothing_when_ComposeText_is_blank()
    {
        var sms = new Mock<ISmsService>();
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "555", undoSendWindow: TimeSpan.Zero)
        {
            ComposeText = "   "
        };

        await viewModel.SendCommand.ExecuteAsync(null);

        sms.Verify(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SendCommand_sends_and_clears_ComposeText()
    {
        var sms = new Mock<ISmsService>();
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "5550148890", undoSendWindow: TimeSpan.Zero)
        {
            ComposeText = "See you then"
        };

        await viewModel.SendCommand.ExecuteAsync(null);

        sms.Verify(s => s.SendAsync("5550148890", "See you then"), Times.Once);
        Assert.Equal(string.Empty, viewModel.ComposeText);
    }

    [Fact]
    public async Task SendCommand_sends_mms_with_caption_when_an_attachment_is_passed()
    {
        var sms = new Mock<ISmsService>();
        sms.Setup(s => s.GetMessagesAsync(1, null, It.IsAny<int>())).ReturnsAsync(new List<SmsMessage>());
        var attachment = new PickedAttachment { FileName = "photo.jpg", LocalPath = "/tmp/photo.jpg", Kind = AttachmentKind.Image };
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "5550148890", undoSendWindow: TimeSpan.Zero)
        {
            ComposeText = "Check this out"
        };

        await viewModel.SendCommand.ExecuteAsync(attachment);

        sms.Verify(s => s.SendMmsAsync(1, "5550148890", "Check this out", attachment), Times.Once);
        sms.Verify(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        Assert.Equal(string.Empty, viewModel.ComposeText);
    }

    [Fact]
    public async Task SendCommand_sends_mms_with_no_body_when_ComposeText_is_blank()
    {
        var sms = new Mock<ISmsService>();
        sms.Setup(s => s.GetMessagesAsync(1, null, It.IsAny<int>())).ReturnsAsync(new List<SmsMessage>());
        var attachment = new PickedAttachment { FileName = "photo.jpg", LocalPath = "/tmp/photo.jpg", Kind = AttachmentKind.Image };
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "5550148890", undoSendWindow: TimeSpan.Zero);

        await viewModel.SendCommand.ExecuteAsync(attachment);

        sms.Verify(s => s.SendMmsAsync(1, "5550148890", null, attachment), Times.Once);
    }

    [Fact]
    public async Task SendCommand_waits_out_the_undo_window_before_sending()
    {
        var sms = new Mock<ISmsService>();
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "5550148890", undoSendWindow: TimeSpan.FromMinutes(1))
        {
            ComposeText = "See you then"
        };

        var sending = viewModel.SendCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsSendPending);
        Assert.Equal(string.Empty, viewModel.ComposeText);
        sms.Verify(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);

        viewModel.FlushPendingSend();
        await sending;

        sms.Verify(s => s.SendAsync("5550148890", "See you then"), Times.Once);
        Assert.False(viewModel.IsSendPending);
    }

    [Fact]
    public async Task The_chat_list_is_asked_to_refresh_only_after_the_text_has_actually_been_sent()
    {
        var sms = new Mock<ISmsService>();
        var refresher = new ConversationListRefresher();
        var sentBeforeRefresh = false;
        refresher.RefreshRequested += () => sentBeforeRefresh = sms.Invocations.Any(i => i.Method.Name == nameof(ISmsService.SendAsync));
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "5550148890",
            undoSendWindow: TimeSpan.FromMinutes(1), listRefresher: refresher)
        {
            ComposeText = "On my way"
        };

        var sending = viewModel.SendCommand.ExecuteAsync(null);
        viewModel.FlushPendingSend();
        await sending;

        Assert.True(sentBeforeRefresh);
    }

    [Fact]
    public async Task A_sent_message_counts_toward_the_review_prompt()
    {
        var reviewPrompt = new Mock<IReviewPromptService>();
        var viewModel = new ThreadDetailViewModel(new Mock<ISmsService>().Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "5550148890", undoSendWindow: TimeSpan.Zero, reviewPrompt: reviewPrompt.Object)
        {
            ComposeText = "See you then"
        };

        await viewModel.SendCommand.ExecuteAsync(null);

        reviewPrompt.Verify(r => r.RecordSent(), Times.Once);
    }

    [Fact]
    public async Task An_undone_message_does_not_count_toward_the_review_prompt()
    {
        var reviewPrompt = new Mock<IReviewPromptService>();
        var viewModel = new ThreadDetailViewModel(new Mock<ISmsService>().Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "5550148890", undoSendWindow: TimeSpan.FromMinutes(1), reviewPrompt: reviewPrompt.Object)
        {
            ComposeText = "Oops"
        };

        var sending = viewModel.SendCommand.ExecuteAsync(null);
        viewModel.UndoSend();
        await sending;

        reviewPrompt.Verify(r => r.RecordSent(), Times.Never);
    }

    // The page only re-renders when the send handler first yields; if the draft save yields
    // before the undo state is set, the Undo bar never appears.
    [Fact]
    public async Task SendCommand_shows_the_undo_state_before_waiting_on_the_draft_store()
    {
        var sms = new Mock<ISmsService>();
        var draftSaved = new TaskCompletionSource();
        var drafts = new Mock<IDraftRepository>();
        drafts.Setup(d => d.SaveAsync(It.IsAny<long>(), It.IsAny<string>())).Returns(draftSaved.Task);
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "5550148890", undoSendWindow: TimeSpan.FromMinutes(1), drafts: drafts.Object)
        {
            ComposeText = "See you then"
        };

        var sending = viewModel.SendCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsSendPending);
        Assert.Equal(string.Empty, viewModel.ComposeText);

        draftSaved.SetResult();
        viewModel.FlushPendingSend();
        await sending;
        sms.Verify(s => s.SendAsync("5550148890", "See you then"), Times.Once);
    }

    [Fact]
    public async Task UndoSend_cancels_the_send_and_restores_the_text()
    {
        var sms = new Mock<ISmsService>();
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "5550148890", undoSendWindow: TimeSpan.FromMinutes(1))
        {
            ComposeText = "Oops wrong person"
        };

        var sending = viewModel.SendCommand.ExecuteAsync(null);
        viewModel.UndoSend();
        await sending;

        sms.Verify(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        Assert.Equal("Oops wrong person", viewModel.ComposeText);
        Assert.False(viewModel.IsSendPending);
    }

    [Fact]
    public async Task UndoSend_returns_the_attachment_so_it_can_be_restored()
    {
        var sms = new Mock<ISmsService>();
        var attachment = new PickedAttachment { FileName = "photo.jpg", LocalPath = "/tmp/photo.jpg", Kind = AttachmentKind.Image };
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "5550148890", undoSendWindow: TimeSpan.FromMinutes(1));

        var sending = viewModel.SendCommand.ExecuteAsync(attachment);
        var restored = viewModel.UndoSend();
        await sending;

        Assert.Same(attachment, restored);
        sms.Verify(s => s.SendMmsAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<PickedAttachment>()), Times.Never);
    }

    [Fact]
    public async Task UndoSend_keeps_text_typed_after_sending()
    {
        var sms = new Mock<ISmsService>();
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "5550148890", undoSendWindow: TimeSpan.FromMinutes(1))
        {
            ComposeText = "first"
        };

        var sending = viewModel.SendCommand.ExecuteAsync(null);
        viewModel.ComposeText = "second";
        viewModel.UndoSend();
        await sending;

        Assert.Equal("first second", viewModel.ComposeText);
    }

    [Fact]
    public async Task Sending_again_during_the_undo_window_sends_the_earlier_message_right_away()
    {
        var sms = new Mock<ISmsService>();
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "5550148890", undoSendWindow: TimeSpan.FromMinutes(1))
        {
            ComposeText = "one"
        };

        var first = viewModel.SendCommand.ExecuteAsync(null);
        viewModel.ComposeText = "two";
        var second = viewModel.SendCommand.ExecuteAsync(null);
        await first;

        sms.Verify(s => s.SendAsync("5550148890", "one"), Times.Once);
        sms.Verify(s => s.SendAsync("5550148890", "two"), Times.Never);
        Assert.True(viewModel.IsSendPending);

        viewModel.FlushPendingSend();
        await second;
        sms.Verify(s => s.SendAsync("5550148890", "two"), Times.Once);
    }

    [Fact]
    public async Task SendCommand_quotes_the_message_being_replied_to()
    {
        var sms = new Mock<ISmsService>();
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "5550148890", undoSendWindow: TimeSpan.Zero)
        {
            ComposeText = "Yes, see you there",
            ReplyingTo = MakeMessage(7, "Are you still coming Sunday?", DateTimeOffset.UtcNow)
        };

        await viewModel.SendCommand.ExecuteAsync(null);

        sms.Verify(s => s.SendAsync("5550148890", "Re: \"Are you still coming Sunday?\"\nYes, see you there"), Times.Once);
        Assert.Null(viewModel.ReplyingTo);
    }

    [Fact]
    public async Task SendCommand_truncates_a_long_quoted_reply()
    {
        var sms = new Mock<ISmsService>();
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "5550148890", undoSendWindow: TimeSpan.Zero)
        {
            ComposeText = "ok",
            ReplyingTo = MakeMessage(7, new string('a', 60), DateTimeOffset.UtcNow)
        };

        await viewModel.SendCommand.ExecuteAsync(null);

        sms.Verify(s => s.SendAsync("5550148890", $"Re: \"{new string('a', 40)}…\"\nok"), Times.Once);
    }

    [Fact]
    public async Task UndoSend_restores_the_reply_target_and_unquoted_text()
    {
        var sms = new Mock<ISmsService>();
        var target = MakeMessage(7, "Are you still coming Sunday?", DateTimeOffset.UtcNow);
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "5550148890", undoSendWindow: TimeSpan.FromMinutes(1))
        {
            ComposeText = "Yes",
            ReplyingTo = target
        };

        var sending = viewModel.SendCommand.ExecuteAsync(null);
        Assert.Null(viewModel.ReplyingTo);
        viewModel.UndoSend();
        await sending;

        Assert.Equal("Yes", viewModel.ComposeText);
        Assert.Same(target, viewModel.ReplyingTo);
    }

    [Fact]
    public async Task ScheduleSendCommand_quotes_the_message_being_replied_to()
    {
        var scheduler = new Mock<IMessageSchedulerService>();
        var sendAt = DateTimeOffset.UtcNow.AddHours(1);
        var viewModel = new ThreadDetailViewModel(new Mock<ISmsService>().Object, scheduler.Object, threadId: 1, address: "5550148890")
        {
            ComposeText = "Yes",
            ReplyingTo = MakeMessage(7, "Coming Sunday?", DateTimeOffset.UtcNow)
        };

        await viewModel.ScheduleSendCommand.ExecuteAsync(new ScheduledSend(sendAt, ScheduleRepeat.None));

        scheduler.Verify(s => s.ScheduleAsync("5550148890", "Re: \"Coming Sunday?\"\nYes", sendAt, ScheduleRepeat.None), Times.Once);
        Assert.Null(viewModel.ReplyingTo);
    }

    [Fact]
    public async Task ScheduleSendCommand_passes_the_chosen_repeat_to_the_scheduler()
    {
        var scheduler = new Mock<IMessageSchedulerService>();
        var sendAt = DateTimeOffset.UtcNow.AddHours(1);
        var viewModel = new ThreadDetailViewModel(new Mock<ISmsService>().Object, scheduler.Object, threadId: 1, address: "5550148890")
        {
            ComposeText = "Take your vitamins"
        };

        await viewModel.ScheduleSendCommand.ExecuteAsync(new ScheduledSend(sendAt, ScheduleRepeat.Daily));

        scheduler.Verify(s => s.ScheduleAsync("5550148890", "Take your vitamins", sendAt, ScheduleRepeat.Daily), Times.Once);
    }

    [Fact]
    public async Task SendReactionCommand_sends_the_emoji_quoting_the_target_message()
    {
        var sms = new Mock<ISmsService>();
        sms.Setup(s => s.GetMessagesAsync(1, null, It.IsAny<int>())).ReturnsAsync(new List<SmsMessage>());
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "5550148890");

        await viewModel.SendReactionCommand.ExecuteAsync(("👍", "You still coming over"));

        sms.Verify(s => s.SendAsync("5550148890", "👍 to \"You still coming over\""), Times.Once);
    }

    [Fact]
    public async Task SendReactionCommand_truncates_a_long_target_message()
    {
        var sms = new Mock<ISmsService>();
        sms.Setup(s => s.GetMessagesAsync(1, null, It.IsAny<int>())).ReturnsAsync(new List<SmsMessage>());
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "5550148890");
        var longBody = new string('a', 60);

        await viewModel.SendReactionCommand.ExecuteAsync(("❤️", longBody));

        var expectedQuote = new string('a', 40) + "…";
        sms.Verify(s => s.SendAsync("5550148890", $"❤️ to \"{expectedQuote}\""), Times.Once);
    }

    [Fact]
    public async Task SendReactionCommand_reloads_messages_after_sending()
    {
        var sms = new Mock<ISmsService>();
        sms.Setup(s => s.GetMessagesAsync(1, null, It.IsAny<int>())).ReturnsAsync(new List<SmsMessage>
        {
            MakeMessage(1, "👍 to \"hi\"", DateTimeOffset.UtcNow, isOutgoing: true)
        });
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "5550148890");

        await viewModel.SendReactionCommand.ExecuteAsync(("👍", "hi"));

        Assert.Single(viewModel.Messages);
    }

    [Fact]
    public async Task SendCommand_in_a_group_sends_one_group_message_to_everyone()
    {
        var sms = new Mock<ISmsService>();
        var group = new[] { "+14707583374", "+16782628755" };
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 3, address: "+14707583374", undoSendWindow: TimeSpan.Zero, participants: group)
        {
            ComposeText = "On my way"
        };

        await viewModel.SendCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsGroup);
        sms.Verify(s => s.SendGroupAsync(3, It.Is<IReadOnlyList<string>>(a => a.SequenceEqual(group)), "On my way", null), Times.Once);
        sms.Verify(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SendCommand_in_a_group_sends_an_attachment_to_everyone()
    {
        var sms = new Mock<ISmsService>();
        var group = new[] { "+14707583374", "+16782628755" };
        var attachment = new PickedAttachment { FileName = "photo.jpg", LocalPath = "/tmp/photo.jpg", Kind = AttachmentKind.Image };
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 3, address: "+14707583374", undoSendWindow: TimeSpan.Zero, participants: group);

        await viewModel.SendCommand.ExecuteAsync(attachment);

        sms.Verify(s => s.SendGroupAsync(3, It.IsAny<IReadOnlyList<string>>(), null, attachment), Times.Once);
        sms.Verify(s => s.SendMmsAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<PickedAttachment>()), Times.Never);
    }

    [Fact]
    public async Task SendReactionCommand_in_a_group_goes_to_everyone()
    {
        var sms = new Mock<ISmsService>();
        sms.Setup(s => s.GetMessagesAsync(3, null, It.IsAny<int>())).ReturnsAsync(new List<SmsMessage>());
        var group = new[] { "+14707583374", "+16782628755" };
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 3, address: "+14707583374", participants: group);

        await viewModel.SendReactionCommand.ExecuteAsync(("👍", "hi"));

        sms.Verify(s => s.SendGroupAsync(3, It.IsAny<IReadOnlyList<string>>(), "👍 to \"hi\"", null), Times.Once);
    }

    [Fact]
    public async Task ScheduleSendCommand_in_a_group_schedules_one_group_message()
    {
        var scheduler = new Mock<IMessageSchedulerService>();
        var group = new[] { "+14707583374", "+16782628755" };
        var sendAt = DateTimeOffset.UtcNow.AddHours(1);
        var viewModel = new ThreadDetailViewModel(new Mock<ISmsService>().Object, scheduler.Object, threadId: 3, address: "+14707583374", participants: group)
        {
            ComposeText = "later"
        };

        await viewModel.ScheduleSendCommand.ExecuteAsync(new ScheduledSend(sendAt, ScheduleRepeat.None));

        scheduler.Verify(s => s.ScheduleGroupAsync(3, It.Is<IReadOnlyList<string>>(a => a.SequenceEqual(group)), "later", sendAt, ScheduleRepeat.None), Times.Once);
        scheduler.Verify(s => s.ScheduleAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<ScheduleRepeat>()), Times.Never);
        Assert.Equal(string.Empty, viewModel.ComposeText);
    }

    [Fact]
    public void A_single_participant_thread_is_not_a_group()
    {
        var viewModel = new ThreadDetailViewModel(new Mock<ISmsService>().Object, new Mock<IMessageSchedulerService>().Object, threadId: 3, address: "555", participants: new[] { "555" });

        Assert.False(viewModel.IsGroup);
    }

    [Fact]
    public async Task SearchCommand_lists_matching_messages_newest_first()
    {
        var sms = new Mock<ISmsService>();
        var older = MakeMessage(1, "Practice is Tuesday", DateTimeOffset.UtcNow.AddDays(-3));
        var newer = MakeMessage(2, "No practice this Tuesday", DateTimeOffset.UtcNow.AddDays(-1));
        sms.Setup(s => s.SearchMessagesAsync(1, "practice", It.IsAny<int>())).ReturnsAsync(new List<SmsMessage> { newer, older });
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "555");

        await viewModel.SearchCommand.ExecuteAsync("  practice ");

        Assert.Equal(new long[] { 2, 1 }, viewModel.SearchResults.Select(m => m.Id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    public async Task SearchCommand_with_less_than_two_characters_clears_results_without_searching(string query)
    {
        var sms = new Mock<ISmsService>();
        sms.Setup(s => s.SearchMessagesAsync(1, "old", It.IsAny<int>())).ReturnsAsync(new List<SmsMessage> { MakeMessage(1, "old", DateTimeOffset.UtcNow) });
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "555");
        await viewModel.SearchCommand.ExecuteAsync("old");

        await viewModel.SearchCommand.ExecuteAsync(query);

        Assert.Empty(viewModel.SearchResults);
        sms.Verify(s => s.SearchMessagesAsync(1, It.Is<string>(q => q.Length < 2), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task JumpToCommand_loads_messages_on_both_sides_of_the_found_one_and_highlights_it()
    {
        var target = MakeMessage(7, "Practice is Tuesday", DateTimeOffset.UtcNow.AddDays(-30));
        var earlier = MakeMessage(6, "earlier", target.Timestamp.AddMinutes(-5));
        var newerPage = Enumerable.Range(1, 50).Select(i => MakeMessage(100 + i, $"later {i}", target.Timestamp.AddMinutes(i))).ToList();
        var sms = new Mock<ISmsService>();
        sms.Setup(s => s.GetMessagesAsync(1, target.Timestamp.AddMilliseconds(1), It.IsAny<int>()))
            .ReturnsAsync(new List<SmsMessage> { target, earlier });
        sms.Setup(s => s.GetNewerMessagesAsync(1, target.Timestamp, It.IsAny<int>())).ReturnsAsync(newerPage);
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "555");

        await viewModel.JumpToCommand.ExecuteAsync(target);

        Assert.Equal(new long[] { 6, 7, 101 }, viewModel.Messages.Take(3).Select(m => m.Id));
        Assert.Equal(52, viewModel.Messages.Count);
        Assert.Same(target, viewModel.HighlightedMessage);
        Assert.False(viewModel.IsAtLatest); // a full newer page means there may be more after it
    }

    [Fact]
    public async Task JumpToCommand_to_one_of_the_latest_messages_ends_up_at_latest()
    {
        var target = MakeMessage(7, "Practice is Tuesday", DateTimeOffset.UtcNow.AddMinutes(-10));
        var later = MakeMessage(8, "later", target.Timestamp.AddMinutes(5));
        var sms = new Mock<ISmsService>();
        sms.Setup(s => s.GetMessagesAsync(1, target.Timestamp.AddMilliseconds(1), It.IsAny<int>())).ReturnsAsync(new List<SmsMessage> { target });
        sms.Setup(s => s.GetNewerMessagesAsync(1, target.Timestamp, It.IsAny<int>())).ReturnsAsync(new List<SmsMessage> { later });
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "555");

        await viewModel.JumpToCommand.ExecuteAsync(target);

        Assert.Equal(new long[] { 7, 8 }, viewModel.Messages.Select(m => m.Id));
        Assert.True(viewModel.IsAtLatest);
    }

    [Fact]
    public async Task LoadCommand_restores_an_unsent_draft()
    {
        var sms = new Mock<ISmsService>();
        var drafts = new Mock<IDraftRepository>();
        drafts.Setup(d => d.GetAsync(1)).ReturnsAsync("See you at");
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "555", drafts: drafts.Object);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal("See you at", viewModel.ComposeText);
    }

    [Fact]
    public async Task LoadCommand_does_not_overwrite_text_already_typed()
    {
        var drafts = new Mock<IDraftRepository>();
        drafts.Setup(d => d.GetAsync(1)).ReturnsAsync("old draft");
        var viewModel = new ThreadDetailViewModel(new Mock<ISmsService>().Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "555", drafts: drafts.Object)
        {
            ComposeText = "typed now"
        };

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal("typed now", viewModel.ComposeText);
    }

    [Fact]
    public async Task SaveDraftAsync_saves_the_compose_text()
    {
        var drafts = new Mock<IDraftRepository>();
        var viewModel = new ThreadDetailViewModel(new Mock<ISmsService>().Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "555", drafts: drafts.Object)
        {
            ComposeText = "half a thought"
        };

        await viewModel.SaveDraftAsync();

        drafts.Verify(d => d.SaveAsync(1, "half a thought"), Times.Once);
    }

    [Fact]
    public async Task SendCommand_clears_the_draft()
    {
        var drafts = new Mock<IDraftRepository>();
        var viewModel = new ThreadDetailViewModel(new Mock<ISmsService>().Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "555", undoSendWindow: TimeSpan.Zero, drafts: drafts.Object)
        {
            ComposeText = "done"
        };

        await viewModel.SendCommand.ExecuteAsync(null);

        drafts.Verify(d => d.SaveAsync(1, string.Empty), Times.Once);
    }

    [Fact]
    public async Task DeleteMessageCommand_deletes_it_and_removes_it_from_the_conversation()
    {
        var keep = MakeMessage(1, "keep", DateTimeOffset.UtcNow.AddMinutes(-2));
        var remove = MakeMessage(2, "oops", DateTimeOffset.UtcNow.AddMinutes(-1));
        var sms = new Mock<ISmsService>();
        sms.Setup(s => s.GetMessagesAsync(1, null, It.IsAny<int>())).ReturnsAsync(new List<SmsMessage> { remove, keep });
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "555");
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.DeleteMessageCommand.ExecuteAsync(remove);

        sms.Verify(s => s.DeleteMessageAsync(remove), Times.Once);
        Assert.Equal(new long[] { 1 }, viewModel.Messages.Select(m => m.Id));
    }

    [Fact]
    public async Task LoadCommand_shows_reaction_texts_as_badges()
    {
        var original = MakeMessage(1, "Sounds good", DateTimeOffset.UtcNow.AddMinutes(-2), isOutgoing: true);
        var reaction = MakeMessage(2, "Loved “Sounds good”", DateTimeOffset.UtcNow.AddMinutes(-1));
        var sms = new Mock<ISmsService>();
        sms.Setup(s => s.GetMessagesAsync(1, null, It.IsAny<int>())).ReturnsAsync(new List<SmsMessage> { reaction, original });
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "555");

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "❤️" }, viewModel.Messages[0].Reactions);
        Assert.True(viewModel.Messages[1].IsHiddenReaction);
    }
}
