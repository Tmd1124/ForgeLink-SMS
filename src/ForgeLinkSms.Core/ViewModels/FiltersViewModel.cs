using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForgeLinkSms.Core.Data;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Utils;

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

    [ObservableProperty]
    private string? _newFilterIconDataUri;

    // The picture goes into the name text like an emoji; removing it takes its spot out again.
    public void SetNewFilterIcon(string? iconDataUri)
    {
        NewFilterIconDataUri = iconDataUri;
        NewFilterName = iconDataUri is null ? FilterName.WithoutMarker(NewFilterName) : FilterName.WithMarker(NewFilterName);
    }

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
        if (NewFilterIconDataUri is null)
        {
            name = FilterName.WithoutMarker(name);
            if (name.Length == 0)
            {
                return;
            }
        }

        var filter = await _filterRepository.CreateFilterAsync(name, NewFilterColorHex);
        if (NewFilterIconDataUri is { } icon)
        {
            await _filterRepository.SetFilterIconAsync(filter.Id, icon);
            filter.IconDataUri = icon;
        }
        Filters.Add(filter);
        NewFilterName = string.Empty;
        NewFilterColorHex = PresetColors[0];
        NewFilterIconDataUri = null;
    }

    [RelayCommand]
    private async Task ToggleFilterActive(long filterId)
    {
        var filter = Filters.FirstOrDefault(f => f.Id == filterId);
        if (filter is null)
        {
            return;
        }
        await _filterRepository.SetFilterHiddenAsync(filterId, !filter.IsHidden);
        filter.IsHidden = !filter.IsHidden;
    }

    [RelayCommand]
    private async Task SetFilterIcon((long FilterId, string? IconDataUri) args)
    {
        await _filterRepository.SetFilterIconAsync(args.FilterId, args.IconDataUri);
        var filter = Filters.FirstOrDefault(f => f.Id == args.FilterId);
        if (filter is null)
        {
            return;
        }
        filter.IconDataUri = args.IconDataUri;
        var name = args.IconDataUri is null ? FilterName.WithoutMarker(filter.Name) : FilterName.WithMarker(filter.Name).Trim();
        if (name.Length > 0 && name != filter.Name)
        {
            await _filterRepository.RenameFilterAsync(filter.Id, name);
            filter.Name = name;
        }
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
