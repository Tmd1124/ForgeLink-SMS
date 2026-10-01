using ForgeLinkSms.Core.Data;
using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Tests.Data;

public class GroupNameRepositoryTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"group-names-{Guid.NewGuid()}.db3");
    private readonly GroupNameRepository _repository;

    public GroupNameRepositoryTests()
    {
        _repository = new GroupNameRepository(_dbPath);
        _repository.InitializeAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _repository.Dispose();
        File.Delete(_dbPath);
    }

    [Fact]
    public async Task A_named_group_keeps_its_name()
    {
        await _repository.SetNameAsync(7, "  Softball Parents ");

        Assert.Equal("Softball Parents", await _repository.GetNameAsync(7));
        Assert.Equal("Softball Parents", (await _repository.GetAllAsync())[7]);
    }

    [Fact]
    public async Task Clearing_the_name_goes_back_to_the_members()
    {
        await _repository.SetNameAsync(7, "Family");
        await _repository.SetNameAsync(7, "   ");

        Assert.Null(await _repository.GetNameAsync(7));
        Assert.Empty(await _repository.GetAllAsync());
    }

    [Fact]
    public void A_group_title_is_its_own_name_or_its_members()
    {
        var members = new[] { "Kim Donnelly", "Ever Harris" };

        Assert.Equal("Family", GroupNames.Title("Family", members));
        Assert.Equal("Kim & Ever", GroupNames.Title(null, members));
        Assert.Equal("Kim & Ever", GroupNames.Title(" ", members));
    }

    [Fact]
    public void A_group_whose_members_have_not_loaded_yet_has_an_empty_title() =>
        Assert.Equal(string.Empty, GroupNames.Title(null, Array.Empty<string>()));
}
