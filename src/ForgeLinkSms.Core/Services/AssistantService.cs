using ForgeLinkSms.Core.Help;

namespace ForgeLinkSms.Core.Services;

public enum AssistantSource { Fixed, Cloud, Offline, Web }

/// One answer in the Ask ForgeLink chat. Topic is the help page it came from, when there was one; Sources are the web pages
/// behind a web-search answer.
public record AssistantReply(string Text, AssistantSource Source, string? Topic = null, IReadOnlyList<AssistantLink>? Sources = null);

/// Answers a typed question: fixed text for price/refund/privacy, else the cloud model working from the best help
/// topics, else the best help topic itself, so the chat always says something useful. With Googling on (the person's choice,
/// on by default), a question the help pages have nothing on goes to a web search through the server instead; questions about
/// ForgeLink itself stay on the help pages, so they are never answered from random web results. If the search is unavailable
/// or out of its free allowance the chat says so and falls back to the help.
public class AssistantService(AssistantClient client, Func<string> installId, string appVersion)
{
    public const string WebUnavailableNote = "Googling isn't available right now, so this comes from the ForgeLink help instead.\n\n";

    public async Task<AssistantReply> AskAsync(string question, bool web = false)
    {
        question = question.Trim();
        if (AssistantFixedAnswers.Find(question) is { } fixedAnswer)
        {
            return new AssistantReply(fixedAnswer, AssistantSource.Fixed);
        }

        const string NotInHelp = "I couldn't find that in the ForgeLink help. Tap Send a report below and describe what happened, and the developer will see it.";

        if (web && !HelpMatcher.IsAboutApp(question))
        {
            var found = await client.AskAsync(installId(), appVersion, question, Array.Empty<string>(), web: true);
            if (found is { Status: AssistantCloudStatus.Answered, Answer: { } webAnswer })
            {
                return new AssistantReply(webAnswer, AssistantSource.Web, Sources: found.Sources);
            }
            // Not about the app, so the help pages' weak keyword matches would only mislead.
            return new AssistantReply(WebUnavailableNote + NotInHelp, AssistantSource.Offline);
        }

        var excerpts = HelpMatcher.Match(question);
        if (excerpts.Count == 0)
        {
            return new AssistantReply(NotInHelp, AssistantSource.Offline);
        }

        var result = await client.AskAsync(installId(), appVersion, question, excerpts.Select(e => e.Text).ToList());
        if (result is { Status: AssistantCloudStatus.Answered, Answer: { } answer })
        {
            return new AssistantReply(answer, AssistantSource.Cloud, excerpts[0].Title);
        }

        return new AssistantReply($"From the help: {excerpts[0].Text}", AssistantSource.Offline, excerpts[0].Title);
    }
}
