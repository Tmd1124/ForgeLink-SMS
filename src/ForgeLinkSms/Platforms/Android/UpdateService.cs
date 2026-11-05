using System.Text.Json;
using Android.Content;
using Android.Provider;
using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Platforms.Android;

public class UpdateService(UpdateClient client) : IUpdateService
{
    private const string LastCheckKey = "update_last_check";
    private const string DismissedKey = "update_dismissed_code";
    private const string PendingKey = "update_pending";
    private const string AutoKey = "update_auto_check";
    private static readonly TimeSpan MinGap = TimeSpan.FromDays(1);

    private static int InstalledCode => int.TryParse(AppInfo.Current.BuildString, out var code) ? code : 0;

    // Play updates its own apps, and its policy forbids an app replacing itself from elsewhere.
    public bool IsSupported
    {
        get
        {
            var context = global::Android.App.Application.Context;
            var installer = OperatingSystem.IsAndroidVersionAtLeast(30)
                ? context.PackageManager!.GetInstallSourceInfo(context.PackageName!).InstallingPackageName
                : context.PackageManager!.GetInstallerPackageName(context.PackageName!);
            return installer != "com.android.vending";
        }
    }

    public string InstalledVersionName => $"{AppInfo.Current.VersionString} ({AppInfo.Current.BuildString})";

    public bool AutoCheckEnabled
    {
        get => Preferences.Get(AutoKey, false);
        set => Preferences.Set(AutoKey, value);
    }

    public DateTimeOffset? LastCheckedUtc =>
        Preferences.ContainsKey(LastCheckKey) ? DateTimeOffset.FromUnixTimeSeconds(Preferences.Get(LastCheckKey, 0L)) : null;

    public UpdateInfo? PendingUpdate
    {
        get
        {
            var json = Preferences.Get(PendingKey, string.Empty);
            if (json.Length == 0)
            {
                return null;
            }
            try
            {
                var info = JsonSerializer.Deserialize<UpdateInfo>(json);
                return info is not null && info.VersionCode > InstalledCode && info.VersionCode != Preferences.Get(DismissedKey, 0) ? info : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    private static bool _checkedThisLaunch;

    // Static, so it survives the activity being recreated or the app sitting in the background:
    // only a fresh process (the app was not running at all) gets a check.
    public async Task<UpdateInfo?> CheckOnLaunchAsync()
    {
        if (_checkedThisLaunch)
        {
            return null;
        }
        _checkedThisLaunch = true;
        if (!IsSupported || !AutoCheckEnabled)
        {
            return null;
        }
        var outcome = await CheckAsync(force: false);
        return outcome.Update is not null ? PendingUpdate : null;
    }

    public async Task<UpdateCheckOutcome> CheckAsync(bool force)
    {
        if (!IsSupported)
        {
            return new UpdateCheckOutcome(UpdateCheckResult.UpToDate);
        }

        if (!force && LastCheckedUtc is { } last && DateTimeOffset.UtcNow - last < MinGap)
        {
            return PendingUpdate is { } cached
                ? new UpdateCheckOutcome(UpdateCheckResult.Available, cached)
                : new UpdateCheckOutcome(UpdateCheckResult.UpToDate);
        }

        var outcome = await client.CheckAsync(InstalledCode);
        if (outcome.Result == UpdateCheckResult.Failed)
        {
            return outcome;
        }

        Preferences.Set(LastCheckKey, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        if (outcome.Update is { } update)
        {
            Preferences.Set(PendingKey, JsonSerializer.Serialize(update));
            if (force)
            {
                Preferences.Remove(DismissedKey);
            }
        }
        else
        {
            Preferences.Remove(PendingKey);
        }
        return outcome;
    }

    public void Dismiss(UpdateInfo update) => Preferences.Set(DismissedKey, update.VersionCode);

    public async Task<string?> DownloadAndInstallAsync(UpdateInfo update, IProgress<double>? progress)
    {
        var context = global::Android.App.Application.Context;

        if (!context.PackageManager!.CanRequestPackageInstalls())
        {
            var settings = new Intent(Settings.ActionManageUnknownAppSources, global::Android.Net.Uri.Parse("package:" + context.PackageName))
                .AddFlags(ActivityFlags.NewTask);
            context.StartActivity(settings);
            return "Allow ForgeLink to install updates on the screen that just opened, then tap Download again.";
        }

        var dir = new Java.IO.File(context.CacheDir, "updates");
        dir.Mkdirs();
        foreach (var old in dir.ListFiles() ?? [])
        {
            old.Delete();
        }
        var apk = new Java.IO.File(dir, $"forgelink-sms-{update.VersionName}.apk");

        var error = await client.DownloadAsync(update, apk.AbsolutePath, progress);
        if (error is not null)
        {
            return error;
        }

        var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(context, context.PackageName + ".fileprovider", apk);
        var install = new Intent(Intent.ActionView)
            .SetDataAndType(uri, "application/vnd.android.package-archive")
            .AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.NewTask);
        context.StartActivity(install);
        return null;
    }
}
