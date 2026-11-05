using System.Net;
using System.Text;
using System.Text.Json;

namespace ForgeLinkSms.Core.Services;

public enum AssistantCloudStatus { Answered, Limit, Unavailable }

public record AssistantCloudResult(AssistantCloudStatus Status, string? Answer = null);

/// Asks the CloudCompass AI website's free assistant endpoint a typed question, with a few help excerpts to answer from.
/// Sends a random install id and the typed text only: never messages, contacts or phone numbers.
public class AssistantClient
{
    public const string DefaultEndpoint = "https://cloudcompassai.com/api/assistant.php";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _http;
    private readonly string _endpoint;

    public AssistantClient(HttpClient http, string endpoint = DefaultEndpoint)
    {
        _http = http;
        _endpoint = endpoint;
    }

    public async Task<AssistantCloudResult> AskAsync(string installId, string appVersion, string question, IReadOnlyList<string> excerpts)
    {
        var payload = JsonSerializer.Serialize(new { install_id = installId, question, excerpts, app_version = appVersion });
        try
        {
            using var timeout = new CancellationTokenSource(Timeout);
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
                return new AssistantCloudResult(AssistantCloudStatus.Answered, text);
            }
        }
        catch (JsonException)
        {
        }
        return new AssistantCloudResult(AssistantCloudStatus.Unavailable);
    }
}
