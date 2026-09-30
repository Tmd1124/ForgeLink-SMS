using Moq;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.ViewModels;

namespace ForgeLinkSms.Core.Tests.ViewModels;

public class RemindersViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 15, 0, 0, TimeSpan.Zero);

    private static SmsThread Thread(long id, string name) => new()
    {
        Id = id,
        Address = "555",
        DisplayName = name,
        LastMessageBody = "hi",
        LastMessageTimestamp = Now,
        UnreadCount = 0
    };

    private static MessageReminder Reminder(int id, long threadId, string address) =>
        new() { Id = id, ThreadId = threadId, Address = address, MessageKey = $"sms:{id}", Preview = "p", RemindAtUtc = Now.AddHours(id) };

    [Fact]
    public async Task LoadCommand_names_each_reminder_by_its_chat_and_falls_back_to_the_number()
    {
        var reminders = new Mock<IReminderService>();
        reminders.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<MessageReminder> { Reminder(1, 3, "4045550199"), Reminder(2, 9, "7705550101") });
        var threads = new Mock<IThreadService>();
        threads.Setup(t => t.GetThreadsAsync()).ReturnsAsync(new List<SmsThread> { Thread(3, "Kim") });
        var viewModel = new RemindersViewModel(reminders.Object, threads.Object);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "Kim", "7705550101" }, viewModel.Reminders.Select(r => r.Name));
    }

    [Fact]
    public async Task CancelCommand_cancels_and_reloads()
    {
        var reminders = new Mock<IReminderService>();
        reminders.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<MessageReminder>());
        var viewModel = new RemindersViewModel(reminders.Object, new Mock<IThreadService>().Object);

        await viewModel.CancelCommand.ExecuteAsync(4);

        reminders.Verify(r => r.CancelAsync(4), Times.Once);
        reminders.Verify(r => r.GetAllAsync(), Times.Once);
    }
}
