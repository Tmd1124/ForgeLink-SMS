using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForgeLinkSms.Core.Data;
using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.ViewModels;

public partial class FiltersViewModel : ObservableObject
{
    private readonly IFilterRepository _filterRepository;

    public static readonly IReadOnlyList<string> PresetColors = new[]
    {
        "#ef4444", "#f97316", "#eab308", "#22c55e", "#06b6d4", "#6366f1", "#a855f7", "#ec4899"
    };

    public ObservableCollection<Filter> Filters { get; } = new();

    [ObservableProperty]
    private string _newFilterName = string.Empty;

    [ObservableProperty]
    private string _newFilterColorHex = PresetColors[0];

    // An emoji on an empty name leads it ("🏀 " ready for typing); otherwise it's appended as a word.
    public void AddEmoji(string emoji)
    {
        var name = NewFilterName.TrimEnd();
        NewFilterName = name.Length == 0 ? emoji + " " : $"{name} {emoji}";
    }

    public FiltersViewModel(IFilterRepository filterRepository)
    {
        _filterRepository = filterRepository;
    }

    [RelayCommand]
    private async Task Load()
    {
        Filters.Clear();
        foreach (var filter in await _filterRepository.GetAllFiltersAsync())
        {
            Filters.Add(filter);
        }
    }

    [RelayCommand]
    private async Task CreateFilter()
    {
        var name = NewFilterName.Trim();
        if (name.Length == 0)
        {
            return;
        }

        var filter = await _filterRepository.CreateFilterAsync(name, NewFilterColorHex);
        Filters.Add(filter);
        NewFilterName = string.Empty;
        NewFilterColorHex = PresetColors[0];
    }

    [RelayCommand]
    private async Task RenameFilter((long FilterId, string NewName) args)
    {
        var name = args.NewName.Trim();
        if (name.Length == 0)
        {
            return;
        }

        await _filterRepository.RenameFilterAsync(args.FilterId, name);
        var filter = Filters.FirstOrDefault(f => f.Id == args.FilterId);
        if (filter is not null)
        {
            filter.Name = name;
        }
    }

    [RelayCommand]
    private async Task SetFilterColor((long FilterId, string ColorHex) args)
    {
        await _filterRepository.SetFilterColorAsync(args.FilterId, args.ColorHex);
        var filter = Filters.FirstOrDefault(f => f.Id == args.FilterId);
        if (filter is not null)
        {
            filter.ColorHex = args.ColorHex;
        }
    }

    [RelayCommand]
    private async Task DeleteFilter(long filterId)
    {
        await _filterRepository.DeleteFilterAsync(filterId);
        var filter = Filters.FirstOrDefault(f => f.Id == filterId);
        if (filter is not null)
        {
            Filters.Remove(filter);
        }
    }
}
