using Moq;
using ForgeLinkSms.Core.Data;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.ViewModels;

namespace ForgeLinkSms.Core.Tests.ViewModels;

public class ScheduledViewModelTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private readonly Mock<IScheduledMessageRepository> _repository = new();
    private readonly Mock<IMessageSchedulerService> _scheduler = new();

    private ScheduledViewModel MakeViewModel()
    {
        _repository.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<ScheduledMessage>());
        return new ScheduledViewModel(_repository.Object, _scheduler.Object, new Mock<IContactService>().Object);
    }

    [Fact]
    public async Task Saving_an_edit_updates_the_text_time_and_repeat()
    {
        _scheduler.Setup(s => s.UpdateAsync(4, "Practice moved to 7", Now.AddDays(1), ScheduleRepeat.Weekly)).ReturnsAsync(true);
        var viewModel = MakeViewModel();

        await viewModel.SaveEditCommand.ExecuteAsync(new ScheduledEdit(4, "  Practice moved to 7 ", Now.AddDays(1), ScheduleRepeat.Weekly));

        _scheduler.Verify(s => s.UpdateAsync(4, "Practice moved to 7", Now.AddDays(1), ScheduleRepeat.Weekly), Times.Once);
        Assert.Null(viewModel.EditError);
        _repository.Verify(r => r.GetAllAsync(), Times.Once);
    }

    [Theory]
    [InlineData("   ", 1, "Write a message first.")]
    [InlineData("Hi", -1, "Pick a time in the future.")]
    public void An_empty_text_or_a_past_time_cannot_be_saved(string body, int hoursFromNow, string hint)
    {
        Assert.Equal(hint, ScheduledViewModel.ValidateEdit(body, Now.AddHours(hoursFromNow), Now));
        Assert.Null(ScheduledViewModel.ValidateEdit("Hi", Now.AddHours(1), Now));
    }

    [Fact]
    public async Task An_invalid_edit_is_not_saved()
    {
        var viewModel = MakeViewModel();

        await viewModel.SaveEditCommand.ExecuteAsync(new ScheduledEdit(4, "", Now.AddDays(1), ScheduleRepeat.None));

        Assert.Equal("Write a message first.", viewModel.EditError);
        _scheduler.Verify(s => s.UpdateAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<ScheduleRepeat>()), Times.Never);
    }

    [Fact]
    public async Task Editing_a_text_that_already_went_out_says_so()
    {
        _scheduler.Setup(s => s.UpdateAsync(4, It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<ScheduleRepeat>())).ReturnsAsync(false);
        var viewModel = MakeViewModel();

        await viewModel.SaveEditCommand.ExecuteAsync(new ScheduledEdit(4, "Hi", Now.AddDays(1), ScheduleRepeat.None));

        Assert.Equal("This text was already sent.", viewModel.EditError);
    }
}
