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
