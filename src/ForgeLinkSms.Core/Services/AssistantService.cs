using ForgeLinkSms.Core.Help;

namespace ForgeLinkSms.Core.Services;

public enum AssistantSource { Fixed, Cloud, Offline }

/// One answer in the Ask ForgeLink chat. Topic is the help page it came from, when there was one.
public record AssistantReply(string Text, AssistantSource Source, string? Topic = null);

/// Answers a typed question: fixed text for price/refund/privacy, else the cloud model working from the best help
/// topics, else the best help topic itself, so the chat always says something useful.
public class AssistantService(AssistantClient client, Func<string> installId, string appVersion)
{
    public async Task<AssistantReply> AskAsync(string question)
    {
        question = question.Trim();
        if (AssistantFixedAnswers.Find(question) is { } fixedAnswer)
        {
            return new AssistantReply(fixedAnswer, AssistantSource.Fixed);
        }

        var excerpts = HelpMatcher.Match(question);
        if (excerpts.Count == 0)
        {
            return new AssistantReply("I couldn't find that in the ForgeLink help. Tap Send a report below and describe what happened, and the developer will see it.", AssistantSource.Offline);
        }

        var result = await client.AskAsync(installId(), appVersion, question, excerpts.Select(e => e.Text).ToList());
        if (result is { Status: AssistantCloudStatus.Answered, Answer: { } answer })
        {
            return new AssistantReply(answer, AssistantSource.Cloud, excerpts[0].Title);
        }

        return new AssistantReply($"From the help: {excerpts[0].Text}", AssistantSource.Offline, excerpts[0].Title);
    }
}
