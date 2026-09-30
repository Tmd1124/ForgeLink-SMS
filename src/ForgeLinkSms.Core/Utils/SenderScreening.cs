using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Utils;

public static class SenderScreening
{
    private static readonly HashSet<string> TollFreeAreaCodes = new() { "800", "833", "844", "855", "866", "877", "888" };

    // Short codes (e.g. 72975), alphanumeric sender IDs (e.g. "AMAZON"), and toll-free numbers
    // are how businesses send automated texts; ordinary people text from 10-digit numbers.
    public static bool IsAutomatedSender(string address)
    {
        // An email address is a person texting from their mail app, not a business sender ID.
        if (address.Contains('@'))
        {
            return false;
        }

        if (address.Any(char.IsLetter))
        {
            return true;
        }

        var digits = PhoneNumberFormatter.ToComparableDigits(address);
        if (digits.Length is 5 or 6)
        {
            return true;
        }

        return digits.Length == 10 && TollFreeAreaCodes.Contains(digits[..3]);
    }

    public static ConversationLane LaneFor(SmsThread thread, IReadOnlySet<string> allowedAddresses) => LaneFor(
        isContact: !string.IsNullOrWhiteSpace(thread.DisplayName),
        isFavorite: thread.IsFavorite,
        isAllowed: allowedAddresses.Contains(PhoneNumberFormatter.ToComparableDigits(thread.Address)),
        hasOutgoing: thread.HasOutgoing,
        address: thread.Address);

    // Replying to an automated sender (e.g. "Y" to confirm an appointment) doesn't make it a
    // person, so the automated check comes before hasOutgoing.
    public static ConversationLane LaneFor(bool isContact, bool isFavorite, bool isAllowed, bool hasOutgoing, string address)
    {
        if (isContact || isFavorite || isAllowed)
        {
            return ConversationLane.Conversations;
        }
        if (IsAutomatedSender(address))
        {
            return ConversationLane.Updates;
        }
        return hasOutgoing ? ConversationLane.Conversations : ConversationLane.Screener;
    }
}
