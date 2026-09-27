using Moq;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.ViewModels;

namespace ForgeLinkSms.Core.Tests.ViewModels;

public class ComposeViewModelTests
{
    private static ComposeViewModel CreateViewModel(Mock<ISmsService>? sms = null, Mock<IContactService>? contacts = null, Mock<IMessageSchedulerService>? scheduler = null)
    {
        if (contacts is null)
        {
            contacts = new Mock<IContactService>();
            contacts.Setup(c => c.GetAllContactsAsync()).ReturnsAsync(Array.Empty<ContactInfo>());
        }
        return new ComposeViewModel((sms ?? new Mock<ISmsService>()).Object, contacts.Object, (scheduler ?? new Mock<IMessageSchedulerService>()).Object);
    }

    [Fact]
    public void AddRecipientCommand_rejects_invalid_numbers()
    {
        var viewModel = CreateViewModel();

        viewModel.AddRecipientCommand.Execute("abc");

        Assert.Empty(viewModel.Recipients);
    }

    [Fact]
    public void AddRecipientCommand_accepts_valid_numbers_and_dedupes()
    {
        var viewModel = CreateViewModel();

        viewModel.AddRecipientCommand.Execute("5550148890");
        viewModel.AddRecipientCommand.Execute("5550148890");

        Assert.Single(viewModel.Recipients);
    }

    [Fact]
    public void IsGroupSend_is_true_only_with_more_than_one_recipient()
    {
        var viewModel = CreateViewModel();

        Assert.False(viewModel.IsGroupSend);
        viewModel.AddRecipientCommand.Execute("5550148890");
        Assert.False(viewModel.IsGroupSend);
        viewModel.AddRecipientCommand.Execute("5550142231");
        Assert.True(viewModel.IsGroupSend);
    }

    [Fact]
    public async Task SendCommand_sends_individually_to_every_recipient_when_group_chat_is_off()
    {
        var sms = new Mock<ISmsService>();
        var viewModel = CreateViewModel(sms);
        viewModel.SendAsGroup = false;
        viewModel.MessageBody = "hello everyone";
        viewModel.AddRecipientCommand.Execute("5550148890");
        viewModel.AddRecipientCommand.Execute("5550142231");

        await viewModel.SendCommand.ExecuteAsync(null);

        sms.Verify(s => s.SendAsync("5550148890", "hello everyone"), Times.Once);
        sms.Verify(s => s.SendAsync("5550142231", "hello everyone"), Times.Once);
    }

    [Fact]
    public async Task SendCommand_sends_one_group_message_by_default_with_several_recipients()
    {
        var sms = new Mock<ISmsService>();
        var viewModel = CreateViewModel(sms);
        viewModel.MessageBody = "hello everyone";
        viewModel.AddRecipientCommand.Execute("5550148890");
        viewModel.AddRecipientCommand.Execute("5550142231");

        await viewModel.SendCommand.ExecuteAsync(null);

        Assert.True(viewModel.SendAsGroup);
        sms.Verify(s => s.SendGroupAsync(0, It.Is<IReadOnlyList<string>>(a => a.SequenceEqual(new[] { "5550148890", "5550142231" })), "hello everyone", null), Times.Once);
        sms.Verify(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SendCommand_with_one_recipient_is_a_plain_text()
    {
        var sms = new Mock<ISmsService>();
        var viewModel = CreateViewModel(sms);
        viewModel.MessageBody = "hi";
        viewModel.AddRecipientCommand.Execute("5550148890");

        await viewModel.SendCommand.ExecuteAsync(null);

        sms.Verify(s => s.SendAsync("5550148890", "hi"), Times.Once);
        sms.Verify(s => s.SendGroupAsync(It.IsAny<long>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>(), It.IsAny<PickedAttachment?>()), Times.Never);
    }

    private static ContactInfo Contact(string name, string phone) => new() { DisplayName = name, PhoneNumber = phone };

    [Fact]
    public async Task FilteredContacts_matches_by_name_case_insensitive()
    {
        var contacts = new Mock<IContactService>();
        contacts.Setup(c => c.GetAllContactsAsync()).ReturnsAsync(new[]
        {
            Contact("Alice Smith", "5550148890"),
            Contact("Bob Jones", "5550142231")
        });
        var viewModel = CreateViewModel(contacts: contacts);
        await viewModel.LoadContactsCommand.ExecuteAsync(null);

        viewModel.SearchText = "alice";

        Assert.Single(viewModel.FilteredContacts);
        Assert.Equal("Alice Smith", viewModel.FilteredContacts[0].DisplayName);
    }

    [Fact]
    public async Task FilteredContacts_matches_by_phone_number_substring()
    {
        var contacts = new Mock<IContactService>();
        contacts.Setup(c => c.GetAllContactsAsync()).ReturnsAsync(new[]
        {
            Contact("Alice Smith", "5550148890"),
            Contact("Bob Jones", "5550142231")
        });
        var viewModel = CreateViewModel(contacts: contacts);
        await viewModel.LoadContactsCommand.ExecuteAsync(null);

        viewModel.SearchText = "4223";

        Assert.Single(viewModel.FilteredContacts);
        Assert.Equal("Bob Jones", viewModel.FilteredContacts[0].DisplayName);
    }

    [Fact]
    public async Task FilteredContacts_excludes_already_added_recipients()
    {
        var contacts = new Mock<IContactService>();
        contacts.Setup(c => c.GetAllContactsAsync()).ReturnsAsync(new[]
        {
            Contact("Alice Smith", "5550148890"),
            Contact("Bob Jones", "5550142231")
        });
        var viewModel = CreateViewModel(contacts: contacts);
        await viewModel.LoadContactsCommand.ExecuteAsync(null);

        viewModel.AddRecipientCommand.Execute("5550148890");

        Assert.Single(viewModel.FilteredContacts);
        Assert.Equal("Bob Jones", viewModel.FilteredContacts[0].DisplayName);
    }

    [Fact]
    public async Task SelectContactCommand_adds_recipient_and_clears_search()
    {
        var contacts = new Mock<IContactService>();
        contacts.Setup(c => c.GetAllContactsAsync()).ReturnsAsync(new[]
        {
            Contact("Alice Smith", "5550148890")
        });
        var viewModel = CreateViewModel(contacts: contacts);
        await viewModel.LoadContactsCommand.ExecuteAsync(null);
        viewModel.SearchText = "alice";

        viewModel.SelectContactCommand.Execute(viewModel.FilteredContacts[0]);

        Assert.Equal(new[] { "5550148890" }, viewModel.Recipients);
        Assert.Equal(string.Empty, viewModel.SearchText);
    }

    [Fact]
    public async Task FindContact_returns_the_matching_contact_even_after_it_is_added_as_a_recipient()
    {
        var contacts = new Mock<IContactService>();
        contacts.Setup(c => c.GetAllContactsAsync()).ReturnsAsync(new[]
        {
            Contact("Alice Smith", "5550148890")
        });
        var viewModel = CreateViewModel(contacts: contacts);
        await viewModel.LoadContactsCommand.ExecuteAsync(null);
        viewModel.AddRecipientCommand.Execute("5550148890");

        var found = viewModel.FindContact("5550148890");

        Assert.Equal("Alice Smith", found?.DisplayName);
    }

    [Fact]
    public void FindContact_returns_null_for_a_number_with_no_matching_contact()
    {
        var viewModel = CreateViewModel();

        Assert.Null(viewModel.FindContact("5550148890"));
    }

    [Fact]
    public async Task BeginMultiSelectCommand_enters_multi_select_mode_with_that_contact_checked()
    {
        var contacts = new Mock<IContactService>();
        contacts.Setup(c => c.GetAllContactsAsync()).ReturnsAsync(new[] { Contact("Alice Smith", "5550148890") });
        var viewModel = CreateViewModel(contacts: contacts);
        await viewModel.LoadContactsCommand.ExecuteAsync(null);
        var alice = viewModel.FilteredContacts[0];

        viewModel.BeginMultiSelectCommand.Execute(alice);

        Assert.True(viewModel.IsMultiSelecting);
        Assert.True(viewModel.IsContactSelected(alice));
        Assert.Equal(1, viewModel.SelectedCount);
        Assert.Empty(viewModel.Recipients);
    }

    [Fact]
    public async Task ToggleContactSelectionCommand_checks_and_unchecks_a_contact()
    {
        var contacts = new Mock<IContactService>();
        contacts.Setup(c => c.GetAllContactsAsync()).ReturnsAsync(new[] { Contact("Alice Smith", "5550148890") });
        var viewModel = CreateViewModel(contacts: contacts);
        await viewModel.LoadContactsCommand.ExecuteAsync(null);
        var alice = viewModel.FilteredContacts[0];
        viewModel.BeginMultiSelectCommand.Execute(alice);

        viewModel.ToggleContactSelectionCommand.Execute(alice);

        Assert.False(viewModel.IsContactSelected(alice));
        Assert.Equal(0, viewModel.SelectedCount);
    }

    [Fact]
    public async Task ConfirmMultiSelectCommand_adds_every_checked_contact_as_a_recipient_and_exits_selection_mode()
    {
        var contacts = new Mock<IContactService>();
        contacts.Setup(c => c.GetAllContactsAsync()).ReturnsAsync(new[]
        {
            Contact("Alice Smith", "5550148890"),
            Contact("Bob Jones", "5550142231")
        });
        var viewModel = CreateViewModel(contacts: contacts);
        await viewModel.LoadContactsCommand.ExecuteAsync(null);
        var alice = viewModel.FilteredContacts[0];
        var bob = viewModel.FilteredContacts[1];
        viewModel.BeginMultiSelectCommand.Execute(alice);
        viewModel.ToggleContactSelectionCommand.Execute(bob);

        viewModel.ConfirmMultiSelectCommand.Execute(null);

        Assert.Equal(new[] { "5550148890", "5550142231" }, viewModel.Recipients);
        Assert.True(viewModel.IsGroupSend);
        Assert.False(viewModel.IsMultiSelecting);
        Assert.Equal(0, viewModel.SelectedCount);
    }

    [Fact]
    public async Task CancelMultiSelectCommand_discards_the_selection_without_adding_recipients()
    {
        var contacts = new Mock<IContactService>();
        contacts.Setup(c => c.GetAllContactsAsync()).ReturnsAsync(new[] { Contact("Alice Smith", "5550148890") });
        var viewModel = CreateViewModel(contacts: contacts);
        await viewModel.LoadContactsCommand.ExecuteAsync(null);
        var alice = viewModel.FilteredContacts[0];
        viewModel.BeginMultiSelectCommand.Execute(alice);

        viewModel.CancelMultiSelectCommand.Execute(null);

        Assert.False(viewModel.IsMultiSelecting);
        Assert.Equal(0, viewModel.SelectedCount);
        Assert.Empty(viewModel.Recipients);
    }

    [Fact]
    public async Task SendCommand_with_an_attachment_to_one_person_sends_a_picture_message()
    {
        var sms = new Mock<ISmsService>();
        var viewModel = CreateViewModel(sms);
        var photo = new PickedAttachment { FileName = "photo.jpg", LocalPath = "/tmp/photo.jpg", Kind = AttachmentKind.Image };
        viewModel.AddRecipientCommand.Execute("5550148890");

        await viewModel.SendCommand.ExecuteAsync(photo);

        sms.Verify(s => s.SendGroupAsync(0, It.Is<IReadOnlyList<string>>(a => a.SequenceEqual(new[] { "5550148890" })), null, photo), Times.Once);
    }

    [Fact]
    public async Task SendCommand_with_an_attachment_as_separate_texts_sends_one_picture_message_each()
    {
        var sms = new Mock<ISmsService>();
        var viewModel = CreateViewModel(sms);
        viewModel.SendAsGroup = false;
        viewModel.MessageBody = "look";
        var photo = new PickedAttachment { FileName = "photo.jpg", LocalPath = "/tmp/photo.jpg", Kind = AttachmentKind.Image };
        viewModel.AddRecipientCommand.Execute("5550148890");
        viewModel.AddRecipientCommand.Execute("5550142231");

        await viewModel.SendCommand.ExecuteAsync(photo);

        sms.Verify(s => s.SendGroupAsync(0, It.Is<IReadOnlyList<string>>(a => a.SequenceEqual(new[] { "5550148890" })), "look", photo), Times.Once);
        sms.Verify(s => s.SendGroupAsync(0, It.Is<IReadOnlyList<string>>(a => a.SequenceEqual(new[] { "5550142231" })), "look", photo), Times.Once);
    }

    [Fact]
    public async Task ScheduleSendCommand_for_a_group_chat_schedules_one_group_message()
    {
        var scheduler = new Mock<IMessageSchedulerService>();
        var viewModel = CreateViewModel(scheduler: scheduler);
        var sendAt = DateTimeOffset.UtcNow.AddHours(2);
        viewModel.MessageBody = "Dinner at 7";
        viewModel.AddRecipientCommand.Execute("5550148890");
        viewModel.AddRecipientCommand.Execute("5550142231");

        await viewModel.ScheduleSendCommand.ExecuteAsync(sendAt);

        scheduler.Verify(s => s.ScheduleGroupAsync(0, It.Is<IReadOnlyList<string>>(a => a.SequenceEqual(new[] { "5550148890", "5550142231" })), "Dinner at 7", sendAt), Times.Once);
        scheduler.Verify(s => s.ScheduleAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>()), Times.Never);
    }

    [Fact]
    public async Task LastSendSucceeded_reports_whether_the_message_went_out()
    {
        var viewModel = CreateViewModel();
        viewModel.MessageBody = "fwd";

        await viewModel.SendCommand.ExecuteAsync(null);
        Assert.False(viewModel.LastSendSucceeded);

        viewModel.AddRecipientCommand.Execute("5550148890");
        await viewModel.SendCommand.ExecuteAsync(null);
        Assert.True(viewModel.LastSendSucceeded);
    }
}
