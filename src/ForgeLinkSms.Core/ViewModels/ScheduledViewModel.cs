using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForgeLinkSms.Core.Data;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Core.ViewModels;

public partial class ScheduledViewModel : ObservableObject
{
    private readonly IScheduledMessageRepository _repository;
    private readonly IMessageSchedulerService _scheduler;
    private readonly IContactService _contactService;

    private readonly Dictionary<string, string> _displayNames = new();

    public ObservableCollection<ScheduledMessage> ScheduledMessages { get; } = new();

    public ScheduledViewModel(IScheduledMessageRepository repository, IMessageSchedulerService scheduler, IContactService contactService)
    {
        _repository = repository;
        _scheduler = scheduler;
        _contactService = contactService;
    }

    [RelayCommand]
    private async Task Load()
    {
        ScheduledMessages.Clear();
        foreach (var message in await _repository.GetAllAsync())
        {
            ScheduledMessages.Add(message);
            foreach (var recipient in message.Recipients.Where(r => !_displayNames.ContainsKey(r)))
            {
                _displayNames[recipient] = (await _contactService.LookupAsync(recipient))?.DisplayName ?? recipient;
            }
        }
    }

    public string GetDisplayName(string address) => _displayNames.GetValueOrDefault(address, address);

    [ObservableProperty]
    private string? _editError;

    public static string? ValidateEdit(string body, DateTimeOffset sendAtUtc, DateTimeOffset nowUtc) =>
        string.IsNullOrWhiteSpace(body) ? "Write a message first."
        : sendAtUtc <= nowUtc ? "Pick a time in the future."
        : null;

    [RelayCommand]
    private async Task SaveEdit(ScheduledEdit edit)
    {
        EditError = ValidateEdit(edit.Body, edit.SendAtUtc, DateTimeOffset.UtcNow);
        if (EditError is not null)
        {
            return;
        }
        if (!await _scheduler.UpdateAsync(edit.Id, edit.Body.Trim(), edit.SendAtUtc, edit.Repeat))
        {
            EditError = "This text was already sent.";
        }
        await Load();
    }

    [RelayCommand]
    private async Task Cancel(int scheduledMessageId)
    {
        await _scheduler.CancelAsync(scheduledMessageId);
        await Load();
    }
}
