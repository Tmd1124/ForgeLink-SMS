namespace ForgeLinkSms.Core.Help;

/// Answers the assistant never leaves to a model: price, refunds and privacy are promises, so they are fixed text.
public static class AssistantFixedAnswers
{
    private static readonly (string[] Words, string Answer)[] Rules =
    [
        (["refund", "money back", "charged", "chargeback"],
            $"For a refund or a billing problem, email {AzureForge.Email} and include the email you bought with."),
        (["how much", "price", "cost", "subscription", "free", "pay "],
            "ForgeLink SMS is a one-time $4.99 purchase. There is no subscription and no ads."),
        (["privacy", "private", "read my", "see my", "my data", "my messages", "track", "spy", "collect"],
            "Your messages, contacts and settings stay on your phone. The developer does not receive them. Read the full policy at AzureForgeAI.com.")
    ];

    public static string? Find(string question)
    {
        var q = " " + question.ToLowerInvariant() + " ";
        foreach (var (words, answer) in Rules)
        {
            if (words.Any(q.Contains))
            {
                return answer;
            }
        }
        return null;
    }
}
