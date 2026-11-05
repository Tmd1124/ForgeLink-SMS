using Android.Content.PM;
using Android.OS;

namespace ForgeLinkSms.Platforms.Android;

public static class BuildInfo
{
    private static ApplicationInfo App => global::Android.App.Application.Context.ApplicationInfo!;

    // Debug builds are the ones installed straight from the developer's machine; release builds are signed with the upload key.
    public static bool IsDevelopment => (App.Flags & ApplicationInfoFlags.Debuggable) != 0;

    public static string InstallSource
    {
        get
        {
            var context = global::Android.App.Application.Context;
            var installer = OperatingSystem.IsAndroidVersionAtLeast(30)
                ? context.PackageManager!.GetInstallSourceInfo(context.PackageName!).InstallingPackageName
                : context.PackageManager!.GetInstallerPackageName(context.PackageName!);
            return installer switch
            {
                "com.android.vending" => "Google Play",
                null or "" => "Installed directly (not from a store)",
                _ => installer
            };
        }
    }

    public static string Device => $"{Build.Manufacturer} {Build.Model}";

    public static string AndroidVersion => $"Android {Build.VERSION.Release} (API {(int)Build.VERSION.SdkInt})";

    public static string PackageName => global::Android.App.Application.Context.PackageName ?? "";
}
