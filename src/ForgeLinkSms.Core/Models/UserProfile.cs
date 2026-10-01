namespace ForgeLinkSms.Core.Models;

public class UserProfile
{
    public required string DisplayName { get; init; }
    public string? PhotoPath { get; init; }
    public string PhoneNumber { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Street { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string PostalCode { get; init; } = string.Empty;
}

public sealed record PostalAddress(string Street, string City, string State, string PostalCode);

/// What the phone itself knows about its owner; any part can be missing.
public sealed record ProfileDetails(string? DisplayName, string? PhoneNumber, string? Email, PostalAddress? Address, string? PhotoPath);
