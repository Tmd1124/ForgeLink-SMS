using Android.Content;
using Android.Graphics;
using AndroidUri = Android.Net.Uri;
using Color = Android.Graphics.Color;
using Paint = Android.Graphics.Paint;

namespace ForgeLinkSms.Platforms.Android.Widget;

// Widget images travel to the launcher in a size-limited binder parcel, so each is a small circle.
internal static class WidgetBitmaps
{
    public static Bitmap Circle(Context context, string? photoUri, string initials, Color accent, int sizePx = 96)
    {
        if (!string.IsNullOrEmpty(photoUri))
        {
            try
            {
                if (LoadPhoto(context, photoUri, sizePx) is { } photo)
                {
                    return CropToCircle(photo, sizePx);
                }
            }
            catch (Exception)
            {
            }
        }
        return Initials(initials, accent, sizePx);
    }

    private static Bitmap? LoadPhoto(Context context, string photoUri, int sizePx)
    {
        // The chat list carries contact photos as data: URIs for the web view.
        if (photoUri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var comma = photoUri.IndexOf(',');
            var bytes = Convert.FromBase64String(photoUri[(comma + 1)..]);
            return BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length);
        }
        var uri = AndroidUri.Parse(photoUri)!;
        var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
        using (var probe = context.ContentResolver!.OpenInputStream(uri))
        {
            if (probe is null)
            {
                return null;
            }
            BitmapFactory.DecodeStream(probe, null, bounds);
        }
        var sample = 1;
        while (Math.Min(bounds.OutWidth, bounds.OutHeight) / (sample * 2) >= sizePx)
        {
            sample *= 2;
        }
        using var input = context.ContentResolver!.OpenInputStream(uri);
        return input is null ? null : BitmapFactory.DecodeStream(input, null, new BitmapFactory.Options { InSampleSize = sample });
    }

    private static Bitmap CropToCircle(Bitmap source, int sizePx)
    {
        var output = Bitmap.CreateBitmap(sizePx, sizePx, Bitmap.Config.Argb8888!)!;
        using var canvas = new Canvas(output);
        var side = Math.Min(source.Width, source.Height);
        var scale = (float)sizePx / side;
        using var shader = new BitmapShader(source, Shader.TileMode.Clamp!, Shader.TileMode.Clamp!);
        using var matrix = new Matrix();
        matrix.SetScale(scale, scale);
        matrix.PostTranslate(-(source.Width - side) / 2f * scale, -(source.Height - side) / 2f * scale);
        shader.SetLocalMatrix(matrix);
        using var paint = new Paint(PaintFlags.AntiAlias);
        paint.SetShader(shader);
        canvas.DrawCircle(sizePx / 2f, sizePx / 2f, sizePx / 2f, paint);
        source.Recycle();
        return output;
    }

    private static Bitmap Initials(string initials, Color accent, int sizePx)
    {
        var output = Bitmap.CreateBitmap(sizePx, sizePx, Bitmap.Config.Argb8888!)!;
        using var canvas = new Canvas(output);
        using var fill = new Paint(PaintFlags.AntiAlias) { Color = accent };
        canvas.DrawCircle(sizePx / 2f, sizePx / 2f, sizePx / 2f, fill);
        using var text = new Paint(PaintFlags.AntiAlias) { Color = Color.White, TextSize = sizePx * 0.4f, TextAlign = Paint.Align.Center };
        text.SetTypeface(Typeface.DefaultBold);
        var metrics = text.GetFontMetrics()!;
        canvas.DrawText(initials, sizePx / 2f, sizePx / 2f - (metrics.Ascent + metrics.Descent) / 2f, text);
        return output;
    }
}
