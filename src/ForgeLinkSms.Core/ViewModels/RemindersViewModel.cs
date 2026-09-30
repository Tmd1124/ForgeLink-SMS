using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Core.ViewModels;

public partial class RemindersViewModel : ObservableObject
{
    private readonly IReminderService _reminderService;
    private readonly IThreadService _threadService;

    public ObservableCollection<(MessageReminder Reminder, string Name)> Reminders { get; } = new();

    public RemindersViewModel(IReminderService reminderService, IThreadService threadService)
    {
        _reminderService = reminderService;
        _threadService = threadService;
    }

    [RelayCommand]
    private async Task Load()
    {
        Reminders.Clear();
        var reminders = await _reminderService.GetAllAsync();
        if (reminders.Count == 0)
        {
            return;
        }

        var names = (await _threadService.GetThreadsAsync()).ToDictionary(t => t.Id, t => t.DisplayNameOrAddress);
        foreach (var reminder in reminders)
        {
            Reminders.Add((reminder, names.GetValueOrDefault(reminder.ThreadId, reminder.Address)));
        }
    }

    [RelayCommand]
    private async Task Cancel(int reminderId)
    {
        await _reminderService.CancelAsync(reminderId);
        await Load();
    }
}
