using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Utils;

// The user's own details as a vCard 3.0, the same format AttachmentTextFormatter uses for sharing a contact.
public static class OwnCard
{
    public static bool CanShare(UserProfile profile) => PhoneNumberFormatter.ToComparableDigits(profile.PhoneNumber).Length > 0;

    public static bool HasAddress(UserProfile profile) =>
        new[] { profile.Street, profile.City, profile.State, profile.PostalCode }.Any(part => !string.IsNullOrWhiteSpace(part));

    public static string AddressLine(UserProfile profile)
    {
        var region = string.Join(" ", new[] { profile.State, profile.PostalCode }.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()));
        return string.Join(", ", new[] { profile.Street, profile.City, region }.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()));
    }

    public static string ToVCard(UserProfile profile, bool includeAddress)
    {
        var name = profile.DisplayName.Trim();
        var lastSpace = name.LastIndexOf(' ');
        var (given, family) = lastSpace > 0 ? (name[..lastSpace], name[(lastSpace + 1)..]) : (name, string.Empty);
        var digits = PhoneNumberFormatter.ToComparableDigits(profile.PhoneNumber);

        var lines = new List<string> { "BEGIN:VCARD", "VERSION:3.0", $"FN:{Escape(name)}", $"N:{Escape(family)};{Escape(given)};;;" };
        if (digits.Length > 0)
        {
            lines.Add($"TEL;TYPE=CELL:{(digits.Length == 10 ? $"+1{digits}" : $"+{digits}")}");
        }
        if (!string.IsNullOrWhiteSpace(profile.Email))
        {
            lines.Add($"EMAIL:{Escape(profile.Email.Trim())}");
        }
        if (includeAddress && HasAddress(profile))
        {
            lines.Add($"ADR;TYPE=HOME:;;{Escape(profile.Street.Trim())};{Escape(profile.City.Trim())};{Escape(profile.State.Trim())};{Escape(profile.PostalCode.Trim())};");
        }
        lines.Add("END:VCARD");
        return string.Join("\n", lines);
    }

    // vCard 3.0 (RFC 2426) text values escape backslash, comma, semicolon and line breaks.
    private static string Escape(string value) =>
        value.Replace(@"\", @"\\").Replace(",", @"\,").Replace(";", @"\;").Replace("\r\n", @"\n").Replace("\n", @"\n");
}
