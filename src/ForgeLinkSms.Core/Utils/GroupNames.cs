namespace ForgeLinkSms.Core.Utils;

public static class GroupNames
{
    private const int MaxNamesShown = 3;

    // "Kim & Ever", "Kim, Ever & Dare", "Kim, Ever, Dare +2". First names keep a group title
    // short enough for a list row; a number with no contact name is shown as-is.
    /// The group's own name when it has one, otherwise its members.
    public static string Title(string? customName, IReadOnlyList<string> memberNames) =>
        string.IsNullOrWhiteSpace(customName) ? Format(memberNames) : customName.Trim();

    public static string Format(IReadOnlyList<string> names)
    {
        if (names.Count == 0)
        {
            return string.Empty;
        }
        if (names.Count == 1)
        {
            return names[0];
        }

        var shortNames = names.Select(FirstName).ToList();
        if (shortNames.Count <= MaxNamesShown)
        {
            return $"{string.Join(", ", shortNames.Take(shortNames.Count - 1))} & {shortNames[^1]}";
        }
        return $"{string.Join(", ", shortNames.Take(MaxNamesShown))} +{shortNames.Count - MaxNamesShown}";
    }

    private static string FirstName(string name) =>
        name.Any(char.IsLetter) ? name.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0] : name;
}
