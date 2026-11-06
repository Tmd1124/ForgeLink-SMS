using System.Net;
using System.Text;
using System.Text.Json;

namespace ForgeLinkSms.Core.Services;

public enum AssistantCloudStatus { Answered, Limit, Unavailable }

/// A page a web-search answer was based on.
public record AssistantLink(string Title, string Url);

public record AssistantCloudResult(AssistantCloudStatus Status, string? Answer = null, IReadOnlyList<AssistantLink>? Sources = null);

/// Asks the CloudCompass AI website's free assistant endpoint a typed question, with a few help excerpts to answer from.
/// Sends a random install id and the typed text only: never messages, contacts or phone numbers.
public class AssistantClient
{
    public const string DefaultEndpoint = "https://cloudcompassai.com/api/assistant.php";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan WebTimeout = TimeSpan.FromSeconds(35);

    private readonly HttpClient _http;
    private readonly string _endpoint;

    public AssistantClient(HttpClient http, string endpoint = DefaultEndpoint)
    {
        _http = http;
        _endpoint = endpoint;
    }

    /// web asks the server to answer from Google Search instead of the help excerpts; the question then goes to Google.
    public async Task<AssistantCloudResult> AskAsync(string installId, string appVersion, string question, IReadOnlyList<string> excerpts, bool web = false)
    {
        var payload = JsonSerializer.Serialize(new { install_id = installId, question, excerpts, app_version = appVersion, web });
        try
        {
            using var timeout = new CancellationTokenSource(web ? WebTimeout : Timeout);
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync(_endpoint, content, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode == (HttpStatusCode)429)
            {
                return new AssistantCloudResult(AssistantCloudStatus.Limit);
            }
            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? Parse(body) : new AssistantCloudResult(AssistantCloudStatus.Unavailable);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return new AssistantCloudResult(AssistantCloudStatus.Unavailable);
        }
    }

    private static AssistantCloudResult Parse(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            if (root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True
                && root.TryGetProperty("answer", out var answer) && answer.ValueKind == JsonValueKind.String
                && answer.GetString() is { Length: > 0 } text)
            {
                return new AssistantCloudResult(AssistantCloudStatus.Answered, text, ParseSources(root));
            }
        }
        catch (JsonException)
        {
        }
        return new AssistantCloudResult(AssistantCloudStatus.Unavailable);
    }

    // Only plain https links are kept, and at most five, whatever the server sent.
    private static IReadOnlyList<AssistantLink> ParseSources(JsonElement root)
    {
        var links = new List<AssistantLink>();
        if (root.TryGetProperty("sources", out var sources) && sources.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in sources.EnumerateArray())
            {
                var url = item.ValueKind == JsonValueKind.Object && item.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
                if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                {
                    continue;
                }
                var title = item.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String && t.GetString() is { Length: > 0 } text ? text : uri.Host;
                links.Add(new AssistantLink(title, url));
                if (links.Count == 5)
                {
                    break;
                }
            }
        }
        return links;
    }
}
