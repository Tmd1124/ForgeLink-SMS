using Moq;
using ForgeLinkSms.Core.Data;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.ViewModels;

namespace ForgeLinkSms.Core.Tests.ViewModels;

public class FiltersViewModelTests
{
    [Fact]
    public async Task LoadCommand_populates_Filters_from_the_repository()
    {
        var repository = new Mock<IFilterRepository>();
        repository.Setup(r => r.GetAllFiltersAsync()).ReturnsAsync(new List<Filter>
        {
            new() { Id = 1, Name = "Work", ColorHex = "#6366f1" }
        });
        var viewModel = new FiltersViewModel(repository.Object);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Filters);
        Assert.Equal("Work", viewModel.Filters[0].Name);
    }

    [Fact]
    public async Task CreateFilterCommand_creates_with_the_current_name_and_color_and_resets_the_inputs()
    {
        var repository = new Mock<IFilterRepository>();
        repository.Setup(r => r.GetAllFiltersAsync()).ReturnsAsync(new List<Filter>());
        repository.Setup(r => r.CreateFilterAsync("Work", "#ef4444"))
            .ReturnsAsync(new Filter { Id = 1, Name = "Work", ColorHex = "#ef4444" });
        var viewModel = new FiltersViewModel(repository.Object);
        viewModel.NewFilterName = "Work";
        viewModel.NewFilterColorHex = "#ef4444";

        await viewModel.CreateFilterCommand.ExecuteAsync(null);

        repository.Verify(r => r.CreateFilterAsync("Work", "#ef4444"), Times.Once);
        Assert.Single(viewModel.Filters);
        Assert.Equal(string.Empty, viewModel.NewFilterName);
        Assert.Equal(FiltersViewModel.PresetColors[0], viewModel.NewFilterColorHex);
    }

    [Fact]
    public async Task CreateFilterCommand_saves_the_chosen_picture_and_clears_it_afterwards()
    {
        var repository = new Mock<IFilterRepository>();
        repository.Setup(r => r.GetAllFiltersAsync()).ReturnsAsync(new List<Filter>());
        repository.Setup(r => r.CreateFilterAsync("Golf", It.IsAny<string>()))
            .ReturnsAsync(new Filter { Id = 7, Name = "Golf", ColorHex = "#ef4444" });
        var viewModel = new FiltersViewModel(repository.Object) { NewFilterName = "Golf", NewFilterIconDataUri = "data:image/png;base64,AAAA" };

        await viewModel.CreateFilterCommand.ExecuteAsync(null);

        repository.Verify(r => r.SetFilterIconAsync(7, "data:image/png;base64,AAAA"), Times.Once);
        Assert.Equal("data:image/png;base64,AAAA", viewModel.Filters[0].IconDataUri);
        Assert.Null(viewModel.NewFilterIconDataUri);
    }

    [Fact]
    public void SetNewFilterIcon_adds_the_picture_to_the_name_text_and_removing_it_takes_it_out()
    {
        var viewModel = new FiltersViewModel(new Mock<IFilterRepository>().Object) { NewFilterName = "Golf" };

        viewModel.SetNewFilterIcon("data:image/png;base64,AAAA");
        Assert.Equal("Golf 🖼", viewModel.NewFilterName);

        viewModel.SetNewFilterIcon(null);
        Assert.Equal("Golf", viewModel.NewFilterName);
        Assert.Null(viewModel.NewFilterIconDataUri);
    }

    [Fact]
    public void Deleting_the_picture_marker_from_the_new_filters_name_removes_the_picture()
    {
        var viewModel = new FiltersViewModel(new Mock<IFilterRepository>().Object) { NewFilterName = "Golf" };
        viewModel.SetNewFilterIcon("data:image/png;base64,AAAA");

        viewModel.NewFilterName = "Golf";

        Assert.Null(viewModel.NewFilterIconDataUri);
    }

    [Fact]
    public async Task Renaming_a_filter_without_its_picture_marker_removes_the_picture()
    {
        var repository = new Mock<IFilterRepository>();
        repository.Setup(r => r.GetAllFiltersAsync()).ReturnsAsync(new List<Filter> { new() { Id = 3, Name = "Golf 🖼", ColorHex = "#ef4444", IconDataUri = "data:image/png;base64,AAAA" } });
        var viewModel = new FiltersViewModel(repository.Object);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.RenameFilterCommand.ExecuteAsync((3L, "Golf"));
        Assert.Null(viewModel.Filters[0].IconDataUri);
        repository.Verify(r => r.SetFilterIconAsync(3, null), Times.Once);

        await viewModel.RenameFilterCommand.ExecuteAsync((3L, "Golf Club"));
        repository.Verify(r => r.SetFilterIconAsync(3, null), Times.Once);
    }

    [Fact]
    public async Task Editing_a_filter_loads_it_into_the_form_and_Update_saves_the_changes()
    {
        var repository = new Mock<IFilterRepository>();
        repository.Setup(r => r.GetAllFiltersAsync()).ReturnsAsync(new List<Filter> { new() { Id = 3, Name = "Golf", ColorHex = "#ef4444" } });
        var viewModel = new FiltersViewModel(repository.Object);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.StartEditing(viewModel.Filters[0]);
        Assert.Equal(3, viewModel.EditingFilterId);
        Assert.Equal("Golf", viewModel.NewFilterName);

        viewModel.AddEmoji("🏀");
        viewModel.SetNewFilterIcon("data:image/png;base64,AAAA");
        viewModel.NewFilterColorHex = "#22c55e";
        await viewModel.CreateFilterCommand.ExecuteAsync(null);

        repository.Verify(r => r.CreateFilterAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        repository.Verify(r => r.RenameFilterAsync(3, "Golf 🏀 🖼"), Times.Once);
        repository.Verify(r => r.SetFilterColorAsync(3, "#22c55e"), Times.Once);
        repository.Verify(r => r.SetFilterIconAsync(3, "data:image/png;base64,AAAA"), Times.Once);
        Assert.Equal("Golf 🏀 🖼", viewModel.Filters[0].Name);
        Assert.Null(viewModel.EditingFilterId);
        Assert.Equal(string.Empty, viewModel.NewFilterName);
    }

    [Fact]
    public async Task Editing_a_filter_with_a_picture_keeps_the_picture_marker_in_the_name_and_cancel_clears_the_form()
    {
        var repository = new Mock<IFilterRepository>();
        repository.Setup(r => r.GetAllFiltersAsync()).ReturnsAsync(new List<Filter>
        {
            new() { Id = 3, Name = "Golf", ColorHex = "#ef4444", IconDataUri = "data:image/png;base64,AAAA" }
        });
        var viewModel = new FiltersViewModel(repository.Object);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.StartEditing(viewModel.Filters[0]);
        Assert.Equal("Golf 🖼", viewModel.NewFilterName);
        Assert.Equal("data:image/png;base64,AAAA", viewModel.NewFilterIconDataUri);

        viewModel.CancelEditing();
        Assert.Null(viewModel.EditingFilterId);
        Assert.Equal(string.Empty, viewModel.NewFilterName);
        Assert.Null(viewModel.NewFilterIconDataUri);
    }

    [Fact]
    public async Task SetFilterIconCommand_puts_the_picture_in_the_filters_name()
    {
        var repository = new Mock<IFilterRepository>();
        repository.Setup(r => r.GetAllFiltersAsync()).ReturnsAsync(new List<Filter> { new() { Id = 3, Name = "Golf", ColorHex = "#ef4444" } });
        var viewModel = new FiltersViewModel(repository.Object);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.SetFilterIconCommand.ExecuteAsync((3L, (string?)"data:image/png;base64,AAAA"));

        repository.Verify(r => r.RenameFilterAsync(3, "Golf 🖼"), Times.Once);
        Assert.Equal("Golf 🖼", viewModel.Filters[0].Name);
    }

    [Fact]
    public async Task ToggleFilterActiveCommand_flips_whether_the_filter_shows_as_a_tab()
    {
        var repository = new Mock<IFilterRepository>();
        repository.Setup(r => r.GetAllFiltersAsync()).ReturnsAsync(new List<Filter> { new() { Id = 3, Name = "Golf", ColorHex = "#ef4444" } });
        var viewModel = new FiltersViewModel(repository.Object);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.ToggleFilterActiveCommand.ExecuteAsync(3L);
        Assert.True(viewModel.Filters[0].IsHidden);
        repository.Verify(r => r.SetFilterHiddenAsync(3, true), Times.Once);

        await viewModel.ToggleFilterActiveCommand.ExecuteAsync(3L);
        Assert.False(viewModel.Filters[0].IsHidden);
        repository.Verify(r => r.SetFilterHiddenAsync(3, false), Times.Once);
    }

    [Fact]
    public async Task CreateFilterCommand_does_nothing_when_the_name_is_blank()
    {
        var repository = new Mock<IFilterRepository>();
        var viewModel = new FiltersViewModel(repository.Object);
        viewModel.NewFilterName = "   ";

        await viewModel.CreateFilterCommand.ExecuteAsync(null);

        repository.Verify(r => r.CreateFilterAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RenameFilterCommand_renames_in_the_repository_and_in_the_list()
    {
        var repository = new Mock<IFilterRepository>();
        repository.Setup(r => r.GetAllFiltersAsync()).ReturnsAsync(new List<Filter>
        {
            new() { Id = 1, Name = "Work", ColorHex = "#6366f1" }
        });
        var viewModel = new FiltersViewModel(repository.Object);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.RenameFilterCommand.ExecuteAsync((1L, "Personal"));

        repository.Verify(r => r.RenameFilterAsync(1, "Personal"), Times.Once);
        Assert.Equal("Personal", viewModel.Filters[0].Name);
    }

    [Fact]
    public async Task SetFilterColorCommand_updates_the_repository_and_the_list()
    {
        var repository = new Mock<IFilterRepository>();
        repository.Setup(r => r.GetAllFiltersAsync()).ReturnsAsync(new List<Filter>
        {
            new() { Id = 1, Name = "Work", ColorHex = "#6366f1" }
        });
        var viewModel = new FiltersViewModel(repository.Object);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.SetFilterColorCommand.ExecuteAsync((1L, "#ef4444"));

        repository.Verify(r => r.SetFilterColorAsync(1, "#ef4444"), Times.Once);
        Assert.Equal("#ef4444", viewModel.Filters[0].ColorHex);
    }

    [Fact]
    public async Task DeleteFilterCommand_deletes_from_the_repository_and_removes_it_from_the_list()
    {
        var repository = new Mock<IFilterRepository>();
        repository.Setup(r => r.GetAllFiltersAsync()).ReturnsAsync(new List<Filter>
        {
            new() { Id = 1, Name = "Work", ColorHex = "#6366f1" }
        });
        var viewModel = new FiltersViewModel(repository.Object);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.DeleteFilterCommand.ExecuteAsync(1L);

        repository.Verify(r => r.DeleteFilterAsync(1), Times.Once);
        Assert.Empty(viewModel.Filters);
    }

    [Theory]
    [InlineData("", "🏀", "🏀 ")]
    [InlineData("Team", "🏀", "Team 🏀")]
    [InlineData("Team ", "🏀", "Team 🏀")]
    [InlineData("🏀 ", "⚾", "🏀 ⚾")]
    public void AddEmoji_adds_the_emoji_to_the_new_filter_name(string name, string emoji, string expected)
    {
        var viewModel = new FiltersViewModel(new Mock<IFilterRepository>().Object) { NewFilterName = name };

        viewModel.AddEmoji(emoji);

        Assert.Equal(expected, viewModel.NewFilterName);
    }
}
