using System.Text.Json;

namespace ForgeLinkSms.Core.Tests.Assistant;

public sealed record BankQuestion(string Q, string Kind, string? Topic, string[] MustContain, string[] MustNotContain, bool Live)
{
    public override string ToString() => Q;

    public bool ExpectsTopic(string title) =>
        Topic is null || Topic.Split('|').Any(t => string.Equals(t, title, StringComparison.OrdinalIgnoreCase));

    // Each mustContain entry is a group of alternatives written a|b; the answer must hold at least one word from every group.
    public bool AnswerCovers(string answer) =>
        MustContain.All(group => group.Split('|').Any(word => answer.Contains(word, StringComparison.OrdinalIgnoreCase)));
}

public static class QuestionBank
{
    private static readonly Lazy<IReadOnlyList<BankQuestion>> All = new(Load);

    private static IReadOnlyList<BankQuestion> Load()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assistant", "questions.json"));
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<List<BankQuestion>>(json, options)!;
    }

    public static IEnumerable<object[]> Of(string kind) => All.Value.Where(q => q.Kind == kind).Select(q => new object[] { q });

    public static IReadOnlyList<BankQuestion> Live => All.Value.Where(q => q.Live).ToList();
}
