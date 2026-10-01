using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Services;

public interface IProfileService
{
    UserProfile GetProfile();
    void SaveProfile(UserProfile profile);
    Task<string?> PickPhotoAsync();
    Task<string?> GetProfilePhotoDataUriAsync();

    /// The phone owner's own contact card, or null when the phone doesn't have one.
    Task<ProfileDetails?> ReadOwnerDetailsAsync();

    /// The street address at the phone's current location, or null when it can't be found.
    Task<PostalAddress?> LookUpCurrentAddressAsync();
}
