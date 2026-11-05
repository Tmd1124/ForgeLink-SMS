namespace ForgeLinkSms.Core.Services;

public interface IUpdateService
{
    /// False when the app came from Google Play, which updates it itself.
    bool IsSupported { get; }

    string InstalledVersionName { get; }

    /// Whether the app checks the website by itself (once per app launch, at most once a day) and shows a banner.
    bool AutoCheckEnabled { get; set; }

    /// The newest update found that the person hasn't dismissed; null when none.
    UpdateInfo? PendingUpdate { get; }

    DateTimeOffset? LastCheckedUtc { get; }

    /// The automatic check, for a cold start only: the first call after the process starts runs it (at most
    /// once a day) and returns the update to announce, if any. Later calls return null without checking.
    Task<UpdateInfo?> CheckOnLaunchAsync();

    /// Checks the website for a newer build. force skips the "checked recently" and "dismissed" shortcuts.
    Task<UpdateCheckOutcome> CheckAsync(bool force);

    /// Hides the banner for this version; a newer version shows it again.
    void Dismiss(UpdateInfo update);

    /// Downloads, verifies, and hands the APK to Android's installer. Returns an error message, or null.
    Task<string?> DownloadAndInstallAsync(UpdateInfo update, IProgress<double>? progress);
}
