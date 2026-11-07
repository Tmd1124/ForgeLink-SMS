using ForgeLinkSms.Core.Services;
using Xunit.Abstractions;

namespace ForgeLinkSms.Core.Tests.Assistant;

/// Runs only when FORGELINK_LIVE=1; skipped otherwise.
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("FORGELINK_LIVE") != "1")
        {
            Skip = "Live assistant test: set FORGELINK_LIVE=1 to run it against cloudcompassai.com (spends the free daily allowance, about 45 calls, and takes about 5 minutes).";
        }
    }
}

/// Layer 2: the real app logic against the real server, one fresh install id per question. The server allows 60 requests a day per IP, so only the questions flagged live run.
public class AssistantLiveTests(ITestOutputHelper output)
{
    private static readonly string[] NotSure = ["not sure", "couldn't find", "could not find"];

    [LiveFact]
    public async Task The_assistant_answers_every_flagged_question_correctly()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(40) };
        var client = new AssistantClient(http);
        var service = new AssistantService(client, () => Guid.NewGuid().ToString("N"), "2.3 (14)");
        var failures = new List<string>();

        foreach (var q in QuestionBank.Live)
        {
            var reply = await service.AskAsync(q.Q, web: false);
            if (reply.Source == AssistantSource.Offline && q.Kind != "offtopic")
            {
                await Task.Delay(12000);
                reply = await service.AskAsync(q.Q, web: false);
                if (reply.Source == AssistantSource.Offline && await DailyLimitReached(client))
                {
                    failures.Add($"STOPPED at '{q.Q}': the server's daily limit (60 requests per IP) is used up. The questions before this one are valid; run again tomorrow for the rest.");
                    break;
                }
            }
            var text = reply.Text;
            var said = NotSure.Any(n => text.Contains(n, StringComparison.OrdinalIgnoreCase));
            string? problem = q.Kind switch
            {
                "howto" when reply.Source == AssistantSource.Offline => "server did not answer (fell back to help text)",
                "howto" when said => "said it was not sure",
                "howto" when !q.AnswerCovers(text) => $"missing: {string.Join(", ", q.MustContain)}",
                "fixed" when reply.Source != AssistantSource.Fixed => "not the fixed answer",
                "fixed" when !q.AnswerCovers(text) => $"missing: {string.Join(", ", q.MustContain)}",
                "offtopic" when !said => "invented an answer instead of saying it was not sure",
                "inject" when q.MustNotContain.Any(n => text.Contains(n, StringComparison.OrdinalIgnoreCase)) => "followed the injected instruction",
                _ => null
            };
            output.WriteLine($"{(problem is null ? "PASS" : "FAIL")} [{q.Kind}] {q.Q}\n      -> {text.Replace('\n', ' ')}{(problem is null ? "" : $"\n      !! {problem}")}");
            if (problem is not null)
            {
                failures.Add($"[{q.Kind}] {q.Q}: {problem}");
            }
            await Task.Delay(5000);
        }

        Assert.True(failures.Count == 0, $"{failures.Count} of {QuestionBank.Live.Count} failed:\n" + string.Join("\n", failures));
    }

    private static async Task<bool> DailyLimitReached(AssistantClient client) =>
        (await client.AskAsync(Guid.NewGuid().ToString("N"), "2.3 (14)", "hello", ["Chats: your conversations."])).Status == AssistantCloudStatus.Limit;
}
