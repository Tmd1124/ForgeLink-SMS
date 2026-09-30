using ForgeLinkSms.Core.Data;

namespace ForgeLinkSms.Core.Tests.Data;

public class MuteRepositoryTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"mute-test-{Guid.NewGuid()}.db3");
    private readonly MuteRepository _repository;
    private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;

    public MuteRepositoryTests()
    {
        _repository = new MuteRepository(_dbPath);
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
    public async Task A_timed_mute_lasts_until_it_expires()
    {
        await _repository.MuteAsync(4, _now.AddHours(1));

        Assert.True(await _repository.IsMutedAsync(4, _now));
        Assert.False(await _repository.IsMutedAsync(4, _now.AddHours(2)));
    }

    [Fact]
    public async Task Muting_always_never_expires()
    {
        await _repository.MuteAsync(4, null);

        Assert.True(await _repository.IsMutedAsync(4, _now.AddYears(5)));
    }

    [Fact]
    public async Task UnmuteAsync_ends_the_mute()
    {
        await _repository.MuteAsync(4, null);

        await _repository.UnmuteAsync(4);

        Assert.False(await _repository.IsMutedAsync(4, _now));
    }

    [Fact]
    public async Task GetMutedThreadIdsAsync_leaves_out_expired_mutes()
    {
        await _repository.MuteAsync(1, null);
        await _repository.MuteAsync(2, _now.AddHours(1));
        await _repository.MuteAsync(3, _now.AddHours(-1));

        Assert.Equal(new long[] { 1, 2 }, (await _repository.GetMutedThreadIdsAsync(_now)).OrderBy(id => id));
    }

    [Fact]
    public async Task GetAll_returns_every_mute_with_its_end_time()
    {
        await _repository.MuteAsync(4, _now.AddHours(1));
        await _repository.MuteAsync(5, null);

        var all = (await _repository.GetAllAsync()).OrderBy(m => m.ThreadId).ToList();

        Assert.Equal(new long[] { 4, 5 }, all.Select(m => m.ThreadId));
        Assert.NotNull(all[0].UntilUtc);
        Assert.Null(all[1].UntilUtc);
    }
}
