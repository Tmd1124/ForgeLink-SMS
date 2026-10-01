using Android.Provider;
using Android.Telephony;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.Utils;
using AndroidApp = Android.App.Application;
using AndroidUri = Android.Net.Uri;

namespace ForgeLinkSms.Platforms.Android;

public class ProfileService : IProfileService
{
    private const string NameKey = "profile_display_name";
    private const string PhotoKey = "profile_photo_path";
    private const string PhoneKey = "profile_phone";
    private const string EmailKey = "profile_email";
    private const string StreetKey = "profile_street";
    private const string CityKey = "profile_city";
    private const string StateKey = "profile_state";
    private const string PostalCodeKey = "profile_postal_code";

    private readonly ILocationService _location;

    public ProfileService(ILocationService location) => _location = location;

    public UserProfile GetProfile()
    {
        var photoPath = Preferences.Get(PhotoKey, string.Empty);
        return new UserProfile
        {
            DisplayName = Preferences.Get(NameKey, string.Empty),
            PhotoPath = string.IsNullOrEmpty(photoPath) ? null : photoPath,
            PhoneNumber = Preferences.Get(PhoneKey, string.Empty),
            Email = Preferences.Get(EmailKey, string.Empty),
            Street = Preferences.Get(StreetKey, string.Empty),
            City = Preferences.Get(CityKey, string.Empty),
            State = Preferences.Get(StateKey, string.Empty),
            PostalCode = Preferences.Get(PostalCodeKey, string.Empty)
        };
    }

    public void SaveProfile(UserProfile profile)
    {
        Preferences.Set(NameKey, profile.DisplayName);
        if (profile.PhotoPath is not null)
        {
            Preferences.Set(PhotoKey, profile.PhotoPath);
        }
        Preferences.Set(PhoneKey, profile.PhoneNumber);
        Preferences.Set(EmailKey, profile.Email);
        Preferences.Set(StreetKey, profile.Street);
        Preferences.Set(CityKey, profile.City);
        Preferences.Set(StateKey, profile.State);
        Preferences.Set(PostalCodeKey, profile.PostalCode);
    }

    public async Task<string?> PickPhotoAsync()
    {
        var result = await MediaPicker.Default.PickPhotoAsync();
        if (result is null)
        {
            return null;
        }

        var destinationPath = Path.Combine(FileSystem.AppDataDirectory, "profile_photo" + Path.GetExtension(result.FileName));
        using var sourceStream = await result.OpenReadAsync();
        using var destinationStream = File.Create(destinationPath);
        await sourceStream.CopyToAsync(destinationStream);

        return destinationPath;
    }

    public Task<string?> GetProfilePhotoDataUriAsync() => ImageDataUriHelper.ToDataUriAsync(GetProfile().PhotoPath);

    // The owner card is the "Me" entry Android keeps apart from ordinary contacts (Samsung shows it
    // at the top of Contacts as the profile); READ_CONTACTS is enough to read it.
    public async Task<ProfileDetails?> ReadOwnerDetailsAsync()
    {
        // Asked for only here, when the person wants their number filled in; the SMS role doesn't grant it.
        var canReadNumber = await Permissions.RequestAsync<ReadPhoneNumbers>() == PermissionStatus.Granted;
        return await Task.Run(() => ReadOwnerDetails(canReadNumber));
    }

    private sealed class ReadPhoneNumbers : Permissions.BasePlatformPermission
    {
        public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
            new[] { (global::Android.Manifest.Permission.ReadPhoneNumbers, true) };
    }

    private static ProfileDetails? ReadOwnerDetails(bool canReadNumber)
    {
        var context = AndroidApp.Context;
        string? name = null, phone = null, email = null;
        PostalAddress? address = null;

        var dataUri = AndroidUri.WithAppendedPath(ContactsContract.Profile.ContentUri, ContactsContract.Contacts.Data.ContentDirectory)!;
        using (var cursor = context.ContentResolver!.Query(dataUri,
                   new[] { "mimetype", "data1", "data4", "data7", "data8", "data9" }, null, null, null))
        {
            while (cursor?.MoveToNext() == true)
            {
                var value = cursor.GetString(1);
                switch (cursor.GetString(0))
                {
                    case "vnd.android.cursor.item/name":
                        name ??= value;
                        break;
                    case "vnd.android.cursor.item/phone_v2":
                        phone ??= value;
                        break;
                    case "vnd.android.cursor.item/email_v2":
                        email ??= value;
                        break;
                    case "vnd.android.cursor.item/postal-address_v2":
                        address ??= new PostalAddress(cursor.GetString(2) ?? string.Empty, cursor.GetString(3) ?? string.Empty,
                            cursor.GetString(4) ?? string.Empty, cursor.GetString(5) ?? string.Empty);
                        break;
                }
            }
        }

        if (canReadNumber)
        {
            phone ??= SimNumber(context);
        }
        var photo = CopyOwnerPhoto(context);
        return name is null && phone is null && email is null && address is null && photo is null
            ? null
            : new ProfileDetails(name, phone, email, address, photo);
    }

    public async Task<PostalAddress?> LookUpCurrentAddressAsync()
    {
        var location = await _location.GetCurrentLocationAsync();
        if (location is not { } here)
        {
            return null;
        }

        try
        {
            // Android's geocoder asks Google's location service, so this only runs when the person taps the button.
            var place = (await Geocoding.Default.GetPlacemarksAsync(here.Latitude, here.Longitude))?.FirstOrDefault();
            if (place is null)
            {
                return null;
            }
            var street = string.Join(" ", new[] { place.SubThoroughfare, place.Thoroughfare }.Where(p => !string.IsNullOrWhiteSpace(p)));
            return new PostalAddress(street, place.Locality ?? string.Empty, place.AdminArea ?? string.Empty, place.PostalCode ?? string.Empty);
        }
        catch (Exception e) when (e is FeatureNotSupportedException or Java.IO.IOException)
        {
            return null;
        }
    }

    // Many carriers never write the number to the SIM, and Android may refuse to share it; either way it's just missing.
    private static string? SimNumber(global::Android.Content.Context context)
    {
        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(33))
            {
                var subscriptions = (SubscriptionManager?)context.GetSystemService(global::Android.Content.Context.TelephonySubscriptionService);
                var number = subscriptions?.GetPhoneNumber(SubscriptionManager.DefaultSmsSubscriptionId);
                return string.IsNullOrWhiteSpace(number) ? null : number;
            }
            var telephony = (TelephonyManager?)context.GetSystemService(global::Android.Content.Context.TelephonyService);
            var line1 = telephony?.Line1Number;
            return string.IsNullOrWhiteSpace(line1) ? null : line1;
        }
        catch (Java.Lang.SecurityException)
        {
            return null;
        }
    }

    private static string? CopyOwnerPhoto(global::Android.Content.Context context)
    {
        try
        {
            using var cursor = context.ContentResolver!.Query(ContactsContract.Profile.ContentUri!, new[] { "photo_uri" }, null, null, null);
            if (cursor?.MoveToFirst() != true || cursor.GetString(0) is not { } photoUri)
            {
                return null;
            }
            using var input = context.ContentResolver.OpenInputStream(AndroidUri.Parse(photoUri)!);
            if (input is null)
            {
                return null;
            }
            var destination = Path.Combine(FileSystem.AppDataDirectory, "profile_photo_owner.jpg");
            using var output = File.Create(destination);
            input.CopyTo(output);
            return destination;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
