using System.Collections.Concurrent;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;
using AndroidApp = global::Android.App.Application;
using AndroidContactsContract = global::Android.Provider.ContactsContract;
using AndroidUri = global::Android.Net.Uri;
using AndroidIntent = global::Android.Content.Intent;
using AndroidActivityFlags = global::Android.Content.ActivityFlags;

namespace ForgeLinkSms.Platforms.Android;

public class ContactService : IContactService
{
    // Keyed on the raw SMS address string, which is stable for a given thread across
    // reloads. ContactService is registered as a singleton, so this lives for the app's
    // lifetime — an existing contact edited mid-session won't be picked up until restart,
    // an accepted tradeoff for avoiding a redundant ContentResolver query + photo re-encode
    // on every conversations-list reload (including after every trash/archive/favorite/read
    // action). "Not found" results are deliberately NOT cached: unlike an existing contact's
    // photo conversion, a negative PhoneLookup query is cheap, and caching it would mean a
    // number newly added as a contact (e.g. via the "+" avatar button) never resolves to its
    // name/photo for the rest of the session.
    private readonly ConcurrentDictionary<string, ContactInfo?> _lookupCache = new();

    public async Task<ContactInfo?> LookupAsync(string phoneNumber)
    {
        if (_lookupCache.TryGetValue(phoneNumber, out var cached))
        {
            return cached;
        }

        var result = await LookupUncachedAsync(phoneNumber);
        if (result is not null)
        {
            _lookupCache[phoneNumber] = result;
        }
        return result;
    }

    private async Task<ContactInfo?> LookupUncachedAsync(string phoneNumber)
    {
        var context = AndroidApp.Context;
        var uri = AndroidUri.WithAppendedPath(AndroidContactsContract.PhoneLookup.ContentFilterUri, AndroidUri.Encode(phoneNumber));
        // PhotoThumbnailUri, not PhotoUri: PhotoUri is the contact's full, unscaled original photo.
        // Every conversation row embeds this as base64 with no resizing, and the list renders every
        // row at once with no virtualization — a handful of contacts with multi-megabyte photos
        // (common with photos synced from Google/social accounts) made the list slow to load and
        // janky to scroll. The thumbnail is exactly what a 40px avatar needs.
        var projection = new[] { AndroidContactsContract.PhoneLookup.InterfaceConsts.DisplayName, AndroidContactsContract.PhoneLookup.InterfaceConsts.PhotoThumbnailUri };

        using var cursor = context.ContentResolver!.Query(uri!, projection, null, null, null);
        if (cursor is null || !cursor.MoveToFirst())
        {
            return null;
        }

        var name = cursor.GetString(cursor.GetColumnIndexOrThrow(AndroidContactsContract.PhoneLookup.InterfaceConsts.DisplayName));
        var photoIndex = cursor.GetColumnIndex(AndroidContactsContract.PhoneLookup.InterfaceConsts.PhotoThumbnailUri);
        var photoUri = photoIndex >= 0 ? cursor.GetString(photoIndex) : null;

        return new ContactInfo
        {
            DisplayName = name ?? phoneNumber,
            PhoneNumber = phoneNumber,
            PhotoUri = await ToPhotoDataUriAsync(context, photoUri)
        };
    }

    // ContactsContract only ever gives back a content:// URI, which the BlazorWebView can't load
    // as an <img src> (same cross-origin restriction that affected the profile photo). Converting
    // to a data: URI here means every consumer of ContactInfo.PhotoUri gets something renderable.
    private static async Task<string?> ToPhotoDataUriAsync(global::Android.Content.Context context, string? photoUri)
    {
        if (string.IsNullOrEmpty(photoUri))
        {
            return null;
        }

        try
        {
            using var stream = context.ContentResolver!.OpenInputStream(AndroidUri.Parse(photoUri)!);
            if (stream is null)
            {
                return null;
            }

            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            return $"data:image/jpeg;base64,{Convert.ToBase64String(memoryStream.ToArray())}";
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ContactsContract.CommonDataKinds.Phone's TYPE_* constants (stable since API level 5; the
    // .NET binding only carries TypeCustom, so these are the documented Android values directly):
    // 1=Home, 3=Work, 4=FaxWork, 5=FaxHome, 6=Pager, 10=CompanyMain, 18=WorkPager.
    // Numbers explicitly labeled as a landline or a shared office line can't receive a text.
    // Ambiguous labels (Main/Other/Custom) are left allowed — plenty of people's real cell
    // number ends up under one of those, and wrongly blocking it is worse than the reverse.
    private static bool CanReceiveText(int phoneType) => phoneType is not (1 or 3 or 4 or 5 or 6 or 10 or 18);

    public Task<IReadOnlyList<ContactInfo>> GetAllContactsAsync()
    {
        var context = AndroidApp.Context;
        var results = new List<ContactInfo>();
        var projection = new[]
        {
            AndroidContactsContract.CommonDataKinds.Phone.InterfaceConsts.DisplayName,
            AndroidContactsContract.CommonDataKinds.Phone.Number,
            AndroidContactsContract.CommonDataKinds.Phone.InterfaceConsts.PhotoThumbnailUri,
            AndroidContactsContract.CommonDataKinds.Phone.InterfaceConsts.Type
        };

        using var cursor = context.ContentResolver!.Query(AndroidContactsContract.CommonDataKinds.Phone.ContentUri!, projection, null, null, null);
        if (cursor is not null)
        {
            var nameIdx = cursor.GetColumnIndexOrThrow(AndroidContactsContract.CommonDataKinds.Phone.InterfaceConsts.DisplayName);
            var numberIdx = cursor.GetColumnIndexOrThrow(AndroidContactsContract.CommonDataKinds.Phone.Number);
            var photoIdx = cursor.GetColumnIndex(AndroidContactsContract.CommonDataKinds.Phone.InterfaceConsts.PhotoThumbnailUri);
            var typeIdx = cursor.GetColumnIndex(AndroidContactsContract.CommonDataKinds.Phone.InterfaceConsts.Type);

            while (cursor.MoveToNext())
            {
                var displayName = cursor.GetString(nameIdx);
                var number = cursor.GetString(numberIdx);
                if (string.IsNullOrEmpty(number))
                {
                    continue;
                }

                results.Add(new ContactInfo
                {
                    DisplayName = displayName ?? number,
                    PhoneNumber = number,
                    PhotoUri = photoIdx >= 0 ? cursor.GetString(photoIdx) : null,
                    CanReceiveText = typeIdx < 0 || CanReceiveText(cursor.GetInt(typeIdx))
                });
            }
        }

        return Task.FromResult<IReadOnlyList<ContactInfo>>(results);
    }

    public Task AddContactAsync(string phoneNumber)
    {
        var intent = new AndroidIntent(AndroidIntent.ActionInsert, AndroidContactsContract.Contacts.ContentUri);
        intent.PutExtra(AndroidContactsContract.Intents.Insert.Phone, phoneNumber);
        intent.SetFlags(AndroidActivityFlags.NewTask);
        AndroidApp.Context.StartActivity(intent);
        return Task.CompletedTask;
    }

    public Task OpenContactAsync(string phoneNumber)
    {
        var context = AndroidApp.Context;
        var lookupUri = FindContactLookupUri(context, phoneNumber);

        AndroidIntent intent;
        if (lookupUri is not null)
        {
            intent = new AndroidIntent(AndroidIntent.ActionView, lookupUri);
        }
        else
        {
            intent = new AndroidIntent(AndroidIntent.ActionInsert, AndroidContactsContract.Contacts.ContentUri);
            intent.PutExtra(AndroidContactsContract.Intents.Insert.Phone, phoneNumber);
        }
        intent.SetFlags(AndroidActivityFlags.NewTask);
        context.StartActivity(intent);
        return Task.CompletedTask;
    }

    private static AndroidUri? FindContactLookupUri(global::Android.Content.Context context, string phoneNumber)
    {
        var uri = AndroidUri.WithAppendedPath(AndroidContactsContract.PhoneLookup.ContentFilterUri, AndroidUri.Encode(phoneNumber));
        var projection = new[]
        {
            AndroidContactsContract.PhoneLookup.InterfaceConsts.Id,
            AndroidContactsContract.PhoneLookup.InterfaceConsts.LookupKey
        };

        using var cursor = context.ContentResolver!.Query(uri!, projection, null, null, null);
        if (cursor is null || !cursor.MoveToFirst())
        {
            return null;
        }

        var contactId = cursor.GetLong(cursor.GetColumnIndexOrThrow(AndroidContactsContract.PhoneLookup.InterfaceConsts.Id));
        var lookupKey = cursor.GetString(cursor.GetColumnIndexOrThrow(AndroidContactsContract.PhoneLookup.InterfaceConsts.LookupKey));
        return AndroidContactsContract.Contacts.GetLookupUri(contactId, lookupKey);
    }
}
