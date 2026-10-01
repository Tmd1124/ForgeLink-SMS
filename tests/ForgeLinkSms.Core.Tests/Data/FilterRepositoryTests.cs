using ForgeLinkSms.Core.Data;

namespace ForgeLinkSms.Core.Tests.Data;

public class FilterRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly FilterRepository _repository;

    public FilterRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"filter-test-{Guid.NewGuid()}.db3");
        _repository = new FilterRepository(_dbPath);
        _repository.InitializeAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _repository.Dispose();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    [Fact]
    public async Task Filters_come_back_in_the_order_they_were_made_and_new_ones_go_last()
    {
        var a = await _repository.CreateFilterAsync("A", "#111111");
        var b = await _repository.CreateFilterAsync("B", "#222222");
        var c = await _repository.CreateFilterAsync("C", "#333333");

        Assert.Equal(new[] { a.Id, b.Id, c.Id }, (await _repository.GetAllFiltersAsync()).Select(f => f.Id));
    }

    [Fact]
    public async Task ReorderFiltersAsync_saves_a_new_order_and_later_filters_still_go_last()
    {
        var a = await _repository.CreateFilterAsync("A", "#111111");
        var b = await _repository.CreateFilterAsync("B", "#222222");
        var c = await _repository.CreateFilterAsync("C", "#333333");

        await _repository.ReorderFiltersAsync(new[] { c.Id, a.Id, b.Id });
        var d = await _repository.CreateFilterAsync("D", "#444444");

        Assert.Equal(new[] { c.Id, a.Id, b.Id, d.Id }, (await _repository.GetAllFiltersAsync()).Select(f => f.Id));
    }

    [Fact]
    public async Task GetAllFiltersAsync_is_empty_initially()
    {
        var filters = await _repository.GetAllFiltersAsync();

        Assert.Empty(filters);
    }

    [Fact]
    public async Task CreateFilterAsync_returns_the_filter_with_an_assigned_id()
    {
        var filter = await _repository.CreateFilterAsync("Work", "#6366f1");

        Assert.True(filter.Id > 0);
        Assert.Equal("Work", filter.Name);
        Assert.Equal("#6366f1", filter.ColorHex);

        var all = await _repository.GetAllFiltersAsync();
        Assert.Single(all);
    }

    [Fact]
    public async Task RenameFilterAsync_updates_the_name()
    {
        var filter = await _repository.CreateFilterAsync("Work", "#6366f1");

        await _repository.RenameFilterAsync(filter.Id, "Personal");

        var all = await _repository.GetAllFiltersAsync();
        Assert.Equal("Personal", all[0].Name);
    }

    [Fact]
    public async Task SetFilterColorAsync_updates_the_color()
    {
        var filter = await _repository.CreateFilterAsync("Work", "#6366f1");

        await _repository.SetFilterColorAsync(filter.Id, "#ef4444");

        var all = await _repository.GetAllFiltersAsync();
        Assert.Equal("#ef4444", all[0].ColorHex);
    }

    [Fact]
    public async Task DeleteFilterAsync_removes_the_filter()
    {
        var filter = await _repository.CreateFilterAsync("Work", "#6366f1");

        await _repository.DeleteFilterAsync(filter.Id);

        Assert.Empty(await _repository.GetAllFiltersAsync());
    }

    [Fact]
    public async Task DeleteFilterAsync_also_removes_its_assignments()
    {
        var filter = await _repository.CreateFilterAsync("Work", "#6366f1");
        await _repository.AssignFilterAsync(threadId: 1, filterId: filter.Id);

        await _repository.DeleteFilterAsync(filter.Id);

        var assignments = await _repository.GetAllAssignmentsAsync();
        Assert.False(assignments.ContainsKey(1));
    }

    [Fact]
    public async Task AssignFilterAsync_then_GetAllAssignmentsAsync_reflects_it()
    {
        var filter = await _repository.CreateFilterAsync("Work", "#6366f1");

        await _repository.AssignFilterAsync(threadId: 42, filterId: filter.Id);

        var assignments = await _repository.GetAllAssignmentsAsync();
        Assert.True(assignments.ContainsKey(42));
        Assert.Equal(new List<long> { filter.Id }, assignments[42]);
    }

    [Fact]
    public async Task AssignFilterAsync_twice_does_not_create_a_duplicate_assignment()
    {
        var filter = await _repository.CreateFilterAsync("Work", "#6366f1");

        await _repository.AssignFilterAsync(threadId: 42, filterId: filter.Id);
        await _repository.AssignFilterAsync(threadId: 42, filterId: filter.Id);

        var assignments = await _repository.GetAllAssignmentsAsync();
        Assert.Single(assignments[42]);
    }

    [Fact]
    public async Task UnassignFilterAsync_removes_only_that_threads_assignment()
    {
        var filter = await _repository.CreateFilterAsync("Work", "#6366f1");
        await _repository.AssignFilterAsync(threadId: 1, filterId: filter.Id);
        await _repository.AssignFilterAsync(threadId: 2, filterId: filter.Id);

        await _repository.UnassignFilterAsync(threadId: 1, filterId: filter.Id);

        var assignments = await _repository.GetAllAssignmentsAsync();
        Assert.False(assignments.ContainsKey(1));
        Assert.True(assignments.ContainsKey(2));
    }

    [Fact]
    public async Task GetAllAssignmentsAsync_groups_multiple_filters_on_the_same_thread()
    {
        var work = await _repository.CreateFilterAsync("Work", "#6366f1");
        var urgent = await _repository.CreateFilterAsync("Urgent", "#ef4444");
        await _repository.AssignFilterAsync(threadId: 1, filterId: work.Id);
        await _repository.AssignFilterAsync(threadId: 1, filterId: urgent.Id);

        var assignments = await _repository.GetAllAssignmentsAsync();

        Assert.Equal(2, assignments[1].Count);
        Assert.Contains(work.Id, assignments[1]);
        Assert.Contains(urgent.Id, assignments[1]);
    }

    [Fact]
    public async Task RemoveAllAssignmentsForThreadAsync_removes_only_that_threads_assignments()
    {
        var filter = await _repository.CreateFilterAsync("Work", "#6366f1");
        await _repository.AssignFilterAsync(threadId: 1, filterId: filter.Id);
        await _repository.AssignFilterAsync(threadId: 2, filterId: filter.Id);

        await _repository.RemoveAllAssignmentsForThreadAsync(1);

        var assignments = await _repository.GetAllAssignmentsAsync();
        Assert.False(assignments.ContainsKey(1));
        Assert.True(assignments.ContainsKey(2));
    }
}
