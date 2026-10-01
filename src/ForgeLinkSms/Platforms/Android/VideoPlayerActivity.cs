using Android.App;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using ForgeLinkSms.Core.Models;
using AndroidColor = Android.Graphics.Color;
using Button = Android.Widget.Button;
using Space = Android.Widget.Space;
using Orientation = Android.Widget.Orientation;

namespace ForgeLinkSms.Platforms.Android;

// Plays a received video inside the app. Android's own player handles the phone formats MMS videos
// use (3GP, H.263, AMR audio) that the app's web view can't.
[Activity(Theme = "@android:style/Theme.Black.NoTitleBar", Exported = false,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize)]
public class VideoPlayerActivity : Activity
{
    public const string PathExtra = "video_path";
    public const string PartIdExtra = "part_id";
    public const string FileNameExtra = "file_name";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        var path = Intent?.GetStringExtra(PathExtra);
        if (path is null)
        {
            Finish();
            return;
        }

        var root = new FrameLayout(this);
        root.SetBackgroundColor(AndroidColor.Black);
        // Keeps the buttons below the status bar and above the navigation bar.
        root.SetFitsSystemWindows(true);

        var video = new VideoView(this);
        root.AddView(video, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Center));
        var controls = new MediaController(this);
        controls.SetAnchorView(video);
        video.SetMediaController(controls);
        video.SetVideoPath(path);
        video.Prepared += (_, _) => video.Start();
        video.Error += (_, e) =>
        {
            Toast.MakeText(this, "This video can't be played", ToastLength.Short)?.Show();
            e.Handled = true;
        };

        var bar = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        bar.SetPadding(Dp(16), Dp(16), Dp(16), Dp(16));
        var download = Pill("⬇ Download");
        download.Click += async (_, _) =>
        {
            var attachment = new MessageAttachment
            {
                PartId = Intent!.GetLongExtra(PartIdExtra, 0),
                FileName = Intent.GetStringExtra(FileNameExtra) ?? "video",
                Kind = AttachmentKind.Video
            };
            var saved = await new AttachmentSaveService().SaveToDeviceAsync(attachment);
            Toast.MakeText(this, saved ? "Saved" : "Couldn't save the video", ToastLength.Short)?.Show();
        };
        bar.AddView(download);
        bar.AddView(new Space(this), new LinearLayout.LayoutParams(0, 0, 1));
        var close = Pill("✕");
        close.Click += (_, _) => Finish();
        bar.AddView(close);
        root.AddView(bar, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Top));

        SetContentView(root);
    }

    private Button Pill(string text)
    {
        var button = new Button(this) { Text = text, TextSize = 16, Typeface = Typeface.DefaultBold };
        button.SetAllCaps(false);
        button.SetTextColor(AndroidColor.White);
        var background = new global::Android.Graphics.Drawables.GradientDrawable();
        background.SetColor(AndroidColor.Argb(60, 255, 255, 255));
        background.SetCornerRadius(Dp(24));
        button.Background = background;
        button.SetPadding(Dp(18), Dp(10), Dp(18), Dp(10));
        button.SetMinHeight(Dp(44));
        button.SetMinimumHeight(Dp(44));
        return button;
    }

    private int Dp(int value) => (int)(value * (Resources?.DisplayMetrics?.Density ?? 1));
}
