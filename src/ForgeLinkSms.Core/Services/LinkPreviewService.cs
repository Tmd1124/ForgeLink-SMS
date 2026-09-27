using System.Collections.Concurrent;
using System.Text;
using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Services;

public class LinkPreviewService
{
    private const int MaxBytes = 512 * 1024;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(6);

    private readonly HttpClient _http;

    // One fetch per link for the life of the app, shared by every message that shows it.
    private readonly ConcurrentDictionary<string, Task<LinkPreview?>> _previews = new();

    public LinkPreviewService(HttpClient http)
    {
        _http = http;
    }

    public Task<LinkPreview?> GetAsync(string url) => _previews.GetOrAdd(url, FetchAsync);

    private async Task<LinkPreview?> FetchAsync(string url)
    {
        try
        {
            using var timeout = new CancellationTokenSource(Timeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            // Many sites only serve their preview tags to browser-like clients.
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Linux; Android 16) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0 Mobile Safari/537.36");
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType is not ("text/html" or "application/xhtml+xml"))
            {
                return null;
            }

            // The tags live in <head>, so the first half-megabyte is plenty.
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            var buffer = new byte[MaxBytes];
            var read = 0;
            int chunk;
            while (read < MaxBytes && (chunk = await stream.ReadAsync(buffer.AsMemory(read), timeout.Token).ConfigureAwait(false)) > 0)
            {
                read += chunk;
            }
            return OpenGraphParser.Parse(response.RequestMessage?.RequestUri?.ToString() ?? url, Encoding.UTF8.GetString(buffer, 0, read));
        }
        // A preview is optional, and platform handlers throw their own types (Android's
        // MalformedURLException when a link redirects to an intent: URL), so any failure means none.
        catch (Exception)
        {
            return null;
        }
    }
}
