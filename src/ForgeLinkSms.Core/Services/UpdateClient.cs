using System.Security.Cryptography;
using System.Text.Json;

namespace ForgeLinkSms.Core.Services;

public record UpdateInfo(int VersionCode, string VersionName, string ApkUrl, string Sha256, string? Notes);

public enum UpdateCheckResult { UpToDate, Available, Failed }

public record UpdateCheckOutcome(UpdateCheckResult Result, UpdateInfo? Update = null, string? Message = null);

/// Reads the update manifest the CloudCompass AI website serves and compares it with the installed build.
/// Until the app is on Google Play the site is the only place updates come from.
public class UpdateClient
{
    public const string DefaultManifestUrl = "https://cloudcompassai.com/downloads/forgelink-sms/latest.json";
    private const string AllowedHost = "cloudcompassai.com";
    private const string Unreachable = "Couldn't reach the update server. Check your connection and try again.";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _http;
    private readonly string _manifestUrl;

    public UpdateClient(HttpClient http, string manifestUrl = DefaultManifestUrl)
    {
        _http = http;
        _manifestUrl = manifestUrl;
    }

    public async Task<UpdateCheckOutcome> CheckAsync(int installedVersionCode)
    {
        try
        {
            using var timeout = new CancellationTokenSource(Timeout);
            using var response = await _http.GetAsync(_manifestUrl, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckOutcome(UpdateCheckResult.Failed, Message: Unreachable);
            }
            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            if (Parse(body) is not { } info)
            {
                return new UpdateCheckOutcome(UpdateCheckResult.Failed, Message: "The update server sent something ForgeLink couldn't read.");
            }
            return info.VersionCode > installedVersionCode
                ? new UpdateCheckOutcome(UpdateCheckResult.Available, info)
                : new UpdateCheckOutcome(UpdateCheckResult.UpToDate);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return new UpdateCheckOutcome(UpdateCheckResult.Failed, Message: Unreachable);
        }
    }

    /// Downloads the APK to destination and checks it against the manifest's SHA-256.
    /// Returns an error message, or null when the file is good.
    public async Task<string?> DownloadAsync(UpdateInfo update, string destination, IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        try
        {
            using var response = await _http.GetAsync(update.ApkUrl, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return "The download failed. Try again in a moment.";
            }

            var total = response.Content.Headers.ContentLength;
            await using (var source = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false))
            await using (var file = File.Create(destination))
            {
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancel).ConfigureAwait(false)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), cancel).ConfigureAwait(false);
                    done += read;
                    if (total is > 0)
                    {
                        progress?.Report((double)done / total.Value);
                    }
                }
            }

            await using var check = File.OpenRead(destination);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(check, cancel).ConfigureAwait(false));
            if (!hash.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                check.Close();
                File.Delete(destination);
                return "The downloaded file didn't match what the server promised, so it was discarded.";
            }
            return null;
        }
        catch (Exception e) when (e is HttpRequestException or IOException)
        {
            return "The download failed. Check your connection and try again.";
        }
    }

    internal static UpdateInfo? Parse(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            if (!root.TryGetProperty("versionCode", out var code) || !code.TryGetInt32(out var versionCode)
                || Text(root, "versionName") is not { Length: > 0 } name
                || Text(root, "apkUrl") is not { Length: > 0 } apkUrl
                || Text(root, "sha256") is not { Length: 64 } sha)
            {
                return null;
            }

            // Only ever fetch an APK from our own site over https, whatever the manifest says.
            if (!Uri.TryCreate(apkUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
                || !(uri.Host == AllowedHost || uri.Host.EndsWith("." + AllowedHost, StringComparison.Ordinal)))
            {
                return null;
            }

            return new UpdateInfo(versionCode, name, apkUrl, sha, Text(root, "notes"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
