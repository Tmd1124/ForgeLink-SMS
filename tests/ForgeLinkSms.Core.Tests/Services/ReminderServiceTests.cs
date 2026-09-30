using Moq;
using ForgeLinkSms.Core.Data;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Core.Tests.Services;

public class ReminderServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"reminder-test-{Guid.NewGuid()}.db3");
    private readonly ReminderRepository _repository;
    private readonly Mock<IReminderAlarmScheduler> _alarms = new();
    private readonly ReminderService _service;
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 15, 0, 0, TimeSpan.Zero);

    public ReminderServiceTests()
    {
        _repository = new ReminderRepository(_dbPath);
        _repository.InitializeAsync().GetAwaiter().GetResult();
        _service = new ReminderService(_repository, _alarms.Object);
    }

    public void Dispose()
    {
        _repository.Dispose();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private static SmsMessage Message(long id, string body, bool isMms = false, int attachments = 0) => new()
    {
        Id = id,
        ThreadId = 12,
        Address = "4045550199",
        Body = body,
        Timestamp = Now.AddDays(-1),
        IsOutgoing = false,
        Status = SmsMessageStatus.Delivered,
        IsMms = isMms,
        Attachments = Enumerable.Range(0, attachments)
            .Select(i => new MessageAttachment { PartId = i, Kind = AttachmentKind.Image, FileName = $"IMG_{i}.jpg" }).ToList()
    };

    [Fact]
    public async Task Setting_a_reminder_stores_it_and_arms_an_alarm()
    {
        var reminder = await _service.SetAsync(Message(5, "Can you pick up the cake Saturday?"), Now.AddHours(3));

        var stored = Assert.Single(await _service.GetAllAsync());
        Assert.Equal((12L, "4045550199", "sms:5", "Can you pick up the cake Saturday?", Now.AddHours(3)),
            (stored.ThreadId, stored.Address, stored.MessageKey, stored.Preview, stored.RemindAtUtc));
        _alarms.Verify(a => a.Arm(reminder.Id, Now.AddHours(3)), Times.Once);
    }

    [Fact]
    public async Task Setting_a_reminder_again_on_the_same_message_moves_it_instead_of_adding_another()
    {
        var first = await _service.SetAsync(Message(5, "hi"), Now.AddHours(3));
        var second = await _service.SetAsync(Message(5, "hi"), Now.AddDays(1));

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(Now.AddDays(1), Assert.Single(await _service.GetAllAsync()).RemindAtUtc);
        _alarms.Verify(a => a.Arm(first.Id, Now.AddDays(1)), Times.Once);
    }

    [Fact]
    public async Task An_sms_and_an_mms_with_the_same_id_get_separate_reminders()
    {
        await _service.SetAsync(Message(5, "text"), Now.AddHours(1));
        await _service.SetAsync(Message(5, "photo", isMms: true), Now.AddHours(2));

        Assert.Equal(2, (await _service.GetAllAsync()).Count);
    }

    [Fact]
    public async Task A_long_message_is_shortened_and_a_photo_without_text_gets_a_label()
    {
        var longOne = await _service.SetAsync(Message(1, new string('a', 300)), Now.AddHours(1));
        var photo = await _service.SetAsync(Message(2, "", isMms: true, attachments: 1), Now.AddHours(1));

        Assert.Equal(new string('a', 120) + "…", longOne.Preview);
        Assert.Equal("📎 Attachment", photo.Preview);
    }

    [Fact]
    public async Task Reminders_are_listed_soonest_first()
    {
        await _service.SetAsync(Message(1, "later"), Now.AddDays(2));
        await _service.SetAsync(Message(2, "sooner"), Now.AddHours(1));

        Assert.Equal(new[] { "sooner", "later" }, (await _service.GetAllAsync()).Select(r => r.Preview));
    }

    [Fact]
    public async Task Reminders_for_one_chat_are_keyed_by_message()
    {
        await _service.SetAsync(Message(1, "a"), Now.AddHours(1));

        var forThread = await _service.GetForThreadAsync(12);

        Assert.Equal(Now.AddHours(1), forThread["sms:1"]);
        Assert.Empty(await _service.GetForThreadAsync(99));
    }

    [Fact]
    public async Task Cancelling_removes_the_reminder_and_disarms_its_alarm()
    {
        var reminder = await _service.SetAsync(Message(1, "a"), Now.AddHours(1));

        await _service.CancelAsync(reminder.Id);

        Assert.Empty(await _service.GetAllAsync());
        _alarms.Verify(a => a.Disarm(reminder.Id), Times.Once);
    }

    [Fact]
    public async Task Firing_returns_the_reminder_once_and_removes_it()
    {
        var reminder = await _service.SetAsync(Message(1, "a"), Now.AddHours(1));

        var fired = await _service.FireAsync(reminder.Id);

        Assert.Equal("a", fired?.Preview);
        Assert.Null(await _service.FireAsync(reminder.Id));
        Assert.Empty(await _service.GetAllAsync());
    }

    [Fact]
    public async Task Rearming_arms_every_pending_reminder()
    {
        var a = await _service.SetAsync(Message(1, "a"), Now.AddHours(1));
        var b = await _service.SetAsync(Message(2, "b"), Now.AddHours(2));
        _alarms.Invocations.Clear();

        await _service.RearmAllAsync();

        _alarms.Verify(x => x.Arm(a.Id, Now.AddHours(1)), Times.Once);
        _alarms.Verify(x => x.Arm(b.Id, Now.AddHours(2)), Times.Once);
    }
}
