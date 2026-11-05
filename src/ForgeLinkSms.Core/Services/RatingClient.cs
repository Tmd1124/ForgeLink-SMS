using System.Text.Json;

namespace ForgeLinkSms.Core.Services;

public enum RatingSendResult { Sent, AlreadySent, Failed }

public record RatingSendOutcome(RatingSendResult Result, string? Message = null);

/// Posts an in-app star rating and optional message to the CloudCompass AI website, which stores it as a
/// pending review for approval. Sends a random install id (never a device or phone identifier).
public class RatingClient
{
    public const string DefaultEndpoint = "https://cloudcompassai.com/api/app_review_submit.php";
    private const string ProductSlug = "forgelink-sms";
    private const string Unreachable = "Couldn't reach the server. Check your connection and try again.";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _http;
    private readonly string _endpoint;

    public RatingClient(HttpClient http, string endpoint = DefaultEndpoint)
    {
        _http = http;
        _endpoint = endpoint;
    }

    public async Task<RatingSendOutcome> SendAsync(string installId, string appVersion, int stars, string? comment, string? name)
    {
        if (stars is < 1 or > 5)
        {
            return new RatingSendOutcome(RatingSendResult.Failed, "Choose 1 to 5 stars.");
        }

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["product"] = ProductSlug,
            ["install_id"] = installId,
            ["stars"] = stars.ToString(),
            ["comment"] = comment?.Trim() ?? "",
            ["name"] = name?.Trim() ?? "",
            ["consent"] = "1",
            ["app_version"] = appVersion
        });

        try
        {
            using var timeout = new CancellationTokenSource(Timeout);
            using var response = await _http.PostAsync(_endpoint, form, timeout.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            return Parse(body);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return new RatingSendOutcome(RatingSendResult.Failed, Unreachable);
        }
    }

    private static RatingSendOutcome Parse(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            if (root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
            {
                var state = root.TryGetProperty("state", out var s) ? s.GetString() : null;
                return new RatingSendOutcome(state == "exists" ? RatingSendResult.AlreadySent : RatingSendResult.Sent);
            }
            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array
                && errors.EnumerateArray().FirstOrDefault().GetString() is { Length: > 0 } first)
            {
                return new RatingSendOutcome(RatingSendResult.Failed, first);
            }
        }
        catch (JsonException)
        {
        }
        return new RatingSendOutcome(RatingSendResult.Failed, Unreachable);
    }
}
