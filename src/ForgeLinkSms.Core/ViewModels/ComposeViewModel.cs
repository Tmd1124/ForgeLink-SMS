using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.ViewModels;

public partial class ComposeViewModel : ObservableObject
{
    private readonly ISmsService _smsService;
    private readonly IContactService _contactService;
    private readonly IMessageSchedulerService _scheduler;
    private readonly List<ContactInfo> _allContacts = new();
    private readonly List<string> _selectedForGroup = new();

    public ObservableCollection<string> Recipients { get; } = new();

    [ObservableProperty]
    private string _messageBody = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredContacts))]
    private string _searchText = string.Empty;

    public bool IsGroupSend => Recipients.Count > 1;

    // With several recipients: one shared group conversation (true) or a separate copy to each person.
    [ObservableProperty]
    private bool _sendAsGroup = true;

    [ObservableProperty]
    private bool _isMultiSelecting;

    public int SelectedCount => _selectedForGroup.Count;

    public bool IsContactSelected(ContactInfo contact) =>
        _selectedForGroup.Contains(PhoneNumberFormatter.ToComparableDigits(contact.PhoneNumber));

    public IReadOnlyList<ContactInfo> FilteredContacts => _allContacts
        .Where(c => !Recipients.Contains(PhoneNumberFormatter.ToComparableDigits(c.PhoneNumber)))
        .Where(c => string.IsNullOrWhiteSpace(SearchText)
            || c.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || c.PhoneNumber.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
        .ToList();

    public ComposeViewModel(ISmsService smsService, IContactService contactService, IMessageSchedulerService scheduler)
    {
        _smsService = smsService;
        _contactService = contactService;
        _scheduler = scheduler;
        Recipients.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsGroupSend));
            OnPropertyChanged(nameof(FilteredContacts));
        };
    }

    [RelayCommand]
    private async Task LoadContacts()
    {
        _allContacts.Clear();
        _allContacts.AddRange(await _contactService.GetAllContactsAsync());
        OnPropertyChanged(nameof(FilteredContacts));
    }

    [RelayCommand]
    private void SelectContact(ContactInfo contact)
    {
        AddRecipient(contact.PhoneNumber);
        SearchText = string.Empty;
    }

    [RelayCommand]
    private void BeginMultiSelect(ContactInfo contact)
    {
        IsMultiSelecting = true;
        ToggleContactSelection(contact);
    }

    [RelayCommand]
    private void ToggleContactSelection(ContactInfo contact)
    {
        var digits = PhoneNumberFormatter.ToComparableDigits(contact.PhoneNumber);
        if (!_selectedForGroup.Remove(digits))
        {
            _selectedForGroup.Add(digits);
        }
        OnPropertyChanged(nameof(SelectedCount));
    }

    [RelayCommand]
    private void ConfirmMultiSelect()
    {
        foreach (var digits in _selectedForGroup)
        {
            if (!Recipients.Contains(digits))
            {
                Recipients.Add(digits);
            }
        }
        _selectedForGroup.Clear();
        IsMultiSelecting = false;
        SearchText = string.Empty;
        OnPropertyChanged(nameof(SelectedCount));
    }

    [RelayCommand]
    private void CancelMultiSelect()
    {
        _selectedForGroup.Clear();
        IsMultiSelecting = false;
        OnPropertyChanged(nameof(SelectedCount));
    }

    [RelayCommand]
    private void AddRecipient(string rawNumber)
    {
        var digits = Regex.Replace(rawNumber, @"[^\d]", "");
        if (digits.Length is not (10 or 11))
        {
            return; // not a valid US-style number; reject silently, UI shows its own validation message
        }
        digits = PhoneNumberFormatter.ToComparableDigits(digits);
        if (!Recipients.Contains(digits))
        {
            Recipients.Add(digits);
        }
    }

    [RelayCommand]
    private void RemoveRecipient(string number) => Recipients.Remove(number);

    public ContactInfo? FindContact(string phoneNumberDigits) =>
        _allContacts.FirstOrDefault(c => PhoneNumberFormatter.ToComparableDigits(c.PhoneNumber) == phoneNumberDigits);

    public bool LastSendSucceeded { get; private set; }

    [RelayCommand]
    private async Task Send(PickedAttachment? attachment)
    {
        LastSendSucceeded = false;
        var text = MessageBody.Trim();
        if ((string.IsNullOrEmpty(text) && attachment is null) || Recipients.Count == 0)
        {
            return;
        }

        var body = string.IsNullOrEmpty(text) ? null : text;
        if (IsGroupSend && SendAsGroup)
        {
            await _smsService.SendGroupAsync(0, Recipients.ToList(), body, attachment);
        }
        else
        {
            // An attachment always needs a picture message; a group send of one is exactly that.
            foreach (var recipient in Recipients)
            {
                if (attachment is not null)
                {
                    await _smsService.SendGroupAsync(0, new[] { recipient }, body, attachment);
                }
                else
                {
                    await _smsService.SendAsync(recipient, text);
                }
            }
        }
        MessageBody = string.Empty;
        LastSendSucceeded = true;
    }

    [RelayCommand]
    private async Task ScheduleSend(ScheduledSend schedule)
    {
        LastSendSucceeded = false;
        var text = MessageBody.Trim();
        if (string.IsNullOrEmpty(text) || Recipients.Count == 0)
        {
            return;
        }

        if (IsGroupSend && SendAsGroup)
        {
            await _scheduler.ScheduleGroupAsync(0, Recipients.ToList(), text, schedule.SendAtUtc, schedule.Repeat);
        }
        else
        {
            foreach (var recipient in Recipients)
            {
                await _scheduler.ScheduleAsync(recipient, text, schedule.SendAtUtc, schedule.Repeat);
            }
        }
        MessageBody = string.Empty;
        LastSendSucceeded = true;
    }
}
