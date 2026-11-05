using System.Net;
using System.Security.Cryptography;
using System.Text;
using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Core.Tests.Services;

public class UpdateClientTests
{
    private const string Sha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private static UpdateClient Make(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new HttpClient(new FakeHandler(respond)), "https://example.test/latest.json");

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string Manifest(int code = 4, string url = "https://cloudcompassai.com/downloads/forgelink-sms/f.apk", string sha = Sha) =>
        $"{{\"versionCode\":{code},\"versionName\":\"1.3\",\"apkUrl\":\"{url}\",\"sha256\":\"{sha}\",\"notes\":\"Fixes\"}}";

    [Fact]
    public async Task A_higher_version_code_is_an_available_update()
    {
        var outcome = await Make(_ => Json(Manifest(4))).CheckAsync(3);

        Assert.Equal(UpdateCheckResult.Available, outcome.Result);
        Assert.Equal("1.3", outcome.Update!.VersionName);
        Assert.Equal("Fixes", outcome.Update.Notes);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public async Task The_same_or_an_older_manifest_is_up_to_date(int installed)
    {
        var outcome = await Make(_ => Json(Manifest(4))).CheckAsync(installed);

        Assert.Equal(UpdateCheckResult.UpToDate, outcome.Result);
        Assert.Null(outcome.Update);
    }

    [Fact]
    public async Task An_apk_url_off_our_site_is_rejected()
    {
        var outcome = await Make(_ => Json(Manifest(url: "https://evil.example/f.apk"))).CheckAsync(3);

        Assert.Equal(UpdateCheckResult.Failed, outcome.Result);
    }

    [Fact]
    public async Task A_plain_http_apk_url_is_rejected()
    {
        var outcome = await Make(_ => Json(Manifest(url: "http://cloudcompassai.com/f.apk"))).CheckAsync(3);

        Assert.Equal(UpdateCheckResult.Failed, outcome.Result);
    }

    [Fact]
    public async Task A_lookalike_host_is_rejected()
    {
        var outcome = await Make(_ => Json(Manifest(url: "https://cloudcompassai.com.evil.example/f.apk"))).CheckAsync(3);

        Assert.Equal(UpdateCheckResult.Failed, outcome.Result);
    }

    [Fact]
    public async Task A_garbled_manifest_fails_without_throwing()
    {
        var outcome = await Make(_ => Json("<html>oops</html>")).CheckAsync(3);

        Assert.Equal(UpdateCheckResult.Failed, outcome.Result);
    }

    [Fact]
    public async Task A_server_error_fails()
    {
        var outcome = await Make(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)).CheckAsync(3);

        Assert.Equal(UpdateCheckResult.Failed, outcome.Result);
    }

    [Fact]
    public async Task An_unreachable_server_fails_with_a_friendly_message()
    {
        var outcome = await Make(_ => throw new HttpRequestException("no route")).CheckAsync(3);

        Assert.Equal(UpdateCheckResult.Failed, outcome.Result);
        Assert.Contains("connection", outcome.Message);
    }

    [Fact]
    public async Task A_download_matching_the_hash_is_kept()
    {
        var bytes = Encoding.UTF8.GetBytes("pretend apk");
        var sha = Convert.ToHexString(SHA256.HashData(bytes));
        var client = Make(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        var path = Path.GetTempFileName();

        var error = await client.DownloadAsync(new UpdateInfo(4, "1.3", "https://cloudcompassai.com/f.apk", sha, null), path);

        Assert.Null(error);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        File.Delete(path);
    }

    [Fact]
    public async Task A_download_with_the_wrong_hash_is_deleted()
    {
        var client = Make(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes("tampered")) });
        var path = Path.GetTempFileName();

        var error = await client.DownloadAsync(new UpdateInfo(4, "1.3", "https://cloudcompassai.com/f.apk", Sha, null), path);

        Assert.NotNull(error);
        Assert.False(File.Exists(path));
    }
}
