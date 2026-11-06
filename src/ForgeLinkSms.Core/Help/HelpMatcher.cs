namespace ForgeLinkSms.Core.Help;

/// A help topic flattened to one block of text, small enough to send as context for an answer.
public sealed record HelpExcerpt(string Title, string Text);

/// Picks the help topics most related to a typed question by simple keyword scoring.
public static class HelpMatcher
{
    private const int MaxExcerptLength = 900;

    private static readonly HashSet<string> Stop = new(StringComparer.Ordinal)
    {
        "the", "and", "how", "can", "does", "did", "what", "why", "when", "where", "who", "not", "will", "was", "are", "you", "your",
        "my", "this", "that", "with", "for", "from", "have", "has", "there", "about", "into", "get", "set", "use", "want", "need", "please"
    };

    // Measured against the help catalog: questions about ForgeLink score 3 or more on their best topic; everyday
    // questions (weather, sports, trivia) score 0 or 1 from words that happen to appear in the help text.
    private const int AboutAppScore = 3;

    /// True when the question is clearly about using ForgeLink, so it belongs on the help pages and never on a web search.
    public static bool IsAboutApp(string question)
    {
        var tokens = Tokens(question);
        return tokens.Count > 0 && HelpCatalog.All.Any(topic => Score(topic, tokens) >= AboutAppScore);
    }

    public static IReadOnlyList<HelpExcerpt> Match(string question, int max = 3)
    {
        var tokens = Tokens(question);
        if (tokens.Count == 0)
        {
            return [];
        }

        return HelpCatalog.All
            .Select(topic => (topic, score: Score(topic, tokens)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .Take(max)
            .Select(x => ToExcerpt(x.topic))
            .ToList();
    }

    private static List<string> Tokens(string question)
    {
        var text = question.ToLowerInvariant().Replace("back up", "backup").Replace("set up", "setup");
        var words = new string(text.Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Where(w => w.Length >= 3 && !Stop.Contains(w)).Distinct().ToList();
    }

    private static int Score(HelpTopic topic, List<string> tokens)
    {
        var score = 0;
        foreach (var token in tokens)
        {
            score += 3 * Hits(topic.Title, token) + 2 * Hits(topic.Summary, token);
            score += topic.Icons.Sum(i => Hits(i.Text, token));
            score += topic.Tips.Sum(t => Hits(t, token));
        }
        return score;
    }

    private static int Hits(string text, string token) =>
        text.ToLowerInvariant()
            .Split([' ', '.', ',', '-', '—', '&', '→', '(', ')', '\'', '’'], StringSplitOptions.RemoveEmptyEntries)
            .Count(word => word.Length >= 3 && (word.StartsWith(token, StringComparison.Ordinal) || token.StartsWith(word, StringComparison.Ordinal)));

    private static HelpExcerpt ToExcerpt(HelpTopic topic)
    {
        var text = $"{topic.Title}: {topic.Summary}";
        if (topic.Icons.Count > 0)
        {
            text += " Icons: " + string.Join("; ", topic.Icons.Select(i => i.Text)) + ".";
        }
        if (topic.Tips.Count > 0)
        {
            text += " Tips: " + string.Join(" ", topic.Tips);
        }
        return new HelpExcerpt(topic.Title, text.Length <= MaxExcerptLength ? text : text[..MaxExcerptLength]);
    }
}
