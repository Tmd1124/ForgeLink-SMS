using ForgeLinkSms.Core.Services;
using AndroidBitmap = global::Android.Graphics.Bitmap;
using AndroidBitmapFactory = global::Android.Graphics.BitmapFactory;

namespace ForgeLinkSms.Platforms.Android;

public class FilterIconPicker : IFilterIconPicker
{
    private const int IconPx = 128;

    private static readonly FilePickerFileType ImageType = new(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        { DevicePlatform.Android, new[] { "image/*" } }
    });

    public async Task<string?> PickIconAsync()
    {
        var result = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Choose a filter picture", FileTypes = ImageType });
        if (result is null)
        {
            return null;
        }

        using var source = await result.OpenReadAsync();
        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer);
        return await Task.Run(() => ToSquareDataUri(buffer.ToArray()));
    }

    // Decoded at reduced size, centered-cropped to a square and stored as a small PNG so it stays cheap to keep in the database.
    private static string? ToSquareDataUri(byte[] bytes)
    {
        try
        {
            var bounds = new AndroidBitmapFactory.Options { InJustDecodeBounds = true };
            AndroidBitmapFactory.DecodeByteArray(bytes, 0, bytes.Length, bounds);
            if (bounds.OutWidth <= 0 || bounds.OutHeight <= 0)
            {
                return null;
            }

            var sample = 1;
            while (Math.Min(bounds.OutWidth, bounds.OutHeight) / (sample * 2) >= IconPx)
            {
                sample *= 2;
            }

            using var decoded = AndroidBitmapFactory.DecodeByteArray(bytes, 0, bytes.Length, new AndroidBitmapFactory.Options { InSampleSize = sample });
            if (decoded is null)
            {
                return null;
            }

            var side = Math.Min(decoded.Width, decoded.Height);
            using var square = AndroidBitmap.CreateBitmap(decoded, (decoded.Width - side) / 2, (decoded.Height - side) / 2, side, side);
            using var icon = AndroidBitmap.CreateScaledBitmap(square, IconPx, IconPx, true);
            using var output = new MemoryStream();
            icon.Compress(AndroidBitmap.CompressFormat.Png!, 100, output);
            return $"data:image/png;base64,{Convert.ToBase64String(output.ToArray())}";
        }
        catch (Exception)
        {
            return null;
        }
    }
}
