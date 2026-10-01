using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Tests.Utils;

public class OwnCardTests
{
    private static UserProfile Profile(string street = "12 Oak St") => new()
    {
        DisplayName = "Travis Donnelly",
        PhoneNumber = "(404) 555-0199",
        Email = "travis@example.com",
        Street = street,
        City = "Atlanta",
        State = "GA",
        PostalCode = "30301"
    };

    [Fact]
    public void Builds_a_contact_card_with_name_number_email_and_address()
    {
        var card = OwnCard.ToVCard(Profile(), includeAddress: true);

        Assert.Equal(string.Join("\n",
            "BEGIN:VCARD",
            "VERSION:3.0",
            "FN:Travis Donnelly",
            "N:Donnelly;Travis;;;",
            "TEL;TYPE=CELL:+14045550199",
            "EMAIL:travis@example.com",
            "ADR;TYPE=HOME:;;12 Oak St;Atlanta;GA;30301;",
            "END:VCARD"), card);
    }

    [Fact]
    public void Leaves_the_address_out_when_asked()
    {
        var card = OwnCard.ToVCard(Profile(), includeAddress: false);

        Assert.DoesNotContain("ADR", card);
        Assert.Contains("TEL;TYPE=CELL:+14045550199", card);
    }

    [Fact]
    public void Skips_lines_for_details_that_are_empty()
    {
        var card = OwnCard.ToVCard(new UserProfile { DisplayName = "Kim", PhoneNumber = "4045550199" }, includeAddress: true);

        Assert.Contains("N:;Kim;;;", card);
        Assert.DoesNotContain("EMAIL", card);
        Assert.DoesNotContain("ADR", card);
    }

    [Fact]
    public void Escapes_characters_that_have_a_meaning_in_contact_cards()
    {
        var card = OwnCard.ToVCard(Profile(street: "Apt 4, 12 Oak St; Rear\\Side"), includeAddress: true);

        Assert.Contains(@"ADR;TYPE=HOME:;;Apt 4\, 12 Oak St\; Rear\\Side;Atlanta;GA;30301;", card);
    }

    [Fact]
    public void A_card_needs_a_phone_number_and_shows_the_address_only_when_there_is_one()
    {
        Assert.True(OwnCard.CanShare(Profile()));
        Assert.False(OwnCard.CanShare(new UserProfile { DisplayName = "Travis" }));
        Assert.True(OwnCard.HasAddress(Profile()));
        Assert.False(OwnCard.HasAddress(new UserProfile { DisplayName = "Travis", City = " " }));
    }

    [Fact]
    public void Formats_the_address_on_one_line_for_showing()
    {
        Assert.Equal("12 Oak St, Atlanta, GA 30301", OwnCard.AddressLine(Profile()));
    }
}
