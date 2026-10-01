using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Tests.Utils;

public class DeferredActionTests
{
    // Each Start waits on its own gate; the test opens it to make the time run out.
    private readonly List<TaskCompletionSource> _gates = new();

    private DeferredAction<long> Make() => new(TimeSpan.FromSeconds(3), (_, token) =>
    {
        var gate = new TaskCompletionSource();
        token.Register(() => gate.TrySetCanceled());
        _gates.Add(gate);
        return gate.Task;
    }, run => run());

    [Fact]
    public async Task Runs_the_action_once_the_time_is_up()
    {
        var deferred = Make();
        var ran = 0;

        await deferred.StartAsync(5, "Deleted", () => { ran++; return Task.CompletedTask; });
        Assert.True(deferred.IsPending(5));
        Assert.Equal(0, ran);

        _gates[0].SetResult();
        await Task.Delay(50);

        Assert.Equal(1, ran);
        Assert.False(deferred.IsPending(5));
    }

    [Fact]
    public async Task Undo_means_the_action_never_runs()
    {
        var deferred = Make();
        var ran = 0;

        await deferred.StartAsync(5, "Deleted", () => { ran++; return Task.CompletedTask; });
        deferred.Undo();
        _gates[0].TrySetResult();
        await Task.Delay(50);

        Assert.Equal(0, ran);
        Assert.False(deferred.IsPending(5));
    }

    [Fact]
    public async Task Starting_another_finishes_the_one_before_it()
    {
        var deferred = Make();
        var ran = new List<long>();

        await deferred.StartAsync(5, "Deleted", () => { ran.Add(5); return Task.CompletedTask; });
        await deferred.StartAsync(6, "Deleted", () => { ran.Add(6); return Task.CompletedTask; });

        Assert.Equal(new long[] { 5 }, ran);
        Assert.True(deferred.IsPending(6));
        Assert.False(deferred.IsPending(5));
    }

    [Fact]
    public async Task Finishing_now_runs_a_waiting_action_straight_away_and_only_once()
    {
        var deferred = Make();
        var ran = 0;

        await deferred.StartAsync(5, "Deleted", () => { ran++; return Task.CompletedTask; });
        await deferred.CommitAsync();
        _gates[0].TrySetResult();
        await Task.Delay(50);

        Assert.Equal(1, ran);
    }

    [Fact]
    public async Task Tells_the_page_when_the_card_should_go()
    {
        var deferred = Make();
        var changes = 0;
        deferred.Changed += () => changes++;

        await deferred.StartAsync(5, "Deleted", () => Task.CompletedTask);
        _gates[0].SetResult();
        await Task.Delay(50);

        Assert.Equal(1, changes);
        Assert.Equal("Deleted", deferred.Label);
    }
}
