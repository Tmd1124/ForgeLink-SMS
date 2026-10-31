namespace ForgeLinkSms.Core.Utils;

// A filter's chosen picture sits inside its name at the picture marker, the way an emoji does;
// screens swap the marker for the image.
public static class FilterName
{
    public const string Marker = "🖼";

    public static string WithMarker(string name)
    {
        var trimmed = name.TrimEnd();
        if (trimmed.Contains(Marker))
        {
            return name;
        }
        return trimmed.Length == 0 ? Marker + " " : $"{trimmed} {Marker}";
    }

    public static string WithoutMarker(string name) =>
        name.Replace(Marker, string.Empty).Replace("  ", " ").Trim();

    /// The text before and after where the picture goes; a name without the marker puts the picture first.
    public static (string Before, string After) Split(string name)
    {
        var index = name.IndexOf(Marker, StringComparison.Ordinal);
        return index < 0 ? (string.Empty, name) : (name[..index], name[(index + Marker.Length)..]);
    }

    /// The name as plain words for spoken labels and messages.
    public static string Plain(string name) => WithoutMarker(name) is { Length: > 0 } words ? words : "picture";
}
