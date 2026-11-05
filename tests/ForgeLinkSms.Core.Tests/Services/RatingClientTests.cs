using System.Net;
using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Core.Tests.Services;

public class RatingClientTests
{
    private const string InstallId = "0b6f1c52-3f4e-4d0a-9a53-6d2f6a3c9e11";

    private sealed class FakeHandler(Func<HttpRequestMessage, string, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public Dictionary<string, string> Form { get; private set; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Form = body.Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split('=', 2))
                .ToDictionary(p => Uri.UnescapeDataString(p[0].Replace('+', ' ')), p => Uri.UnescapeDataString((p.Length > 1 ? p[1] : "").Replace('+', ' ')));
            return await respond(request, body);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string json) =>
        new(code) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private static (RatingClient Client, FakeHandler Handler) Make(Func<HttpRequestMessage, string, Task<HttpResponseMessage>> respond)
    {
        var handler = new FakeHandler(respond);
        return (new RatingClient(new HttpClient(handler), "https://example.test/api/app_review_submit.php"), handler);
    }

    [Fact]
    public async Task Posts_the_rating_as_a_form_to_the_endpoint()
    {
        var (client, handler) = Make((_, _) => Task.FromResult(Json(HttpStatusCode.OK, "{\"ok\":true,\"state\":\"created\"}")));

        var outcome = await client.SendAsync(InstallId, "1.1 (2)", 4, "  Works well  ", " Ann B. ");

        Assert.Equal(RatingSendResult.Sent, outcome.Result);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://example.test/api/app_review_submit.php", handler.Request.RequestUri!.ToString());
        Assert.Equal("forgelink-sms", handler.Form["product"]);
        Assert.Equal(InstallId, handler.Form["install_id"]);
        Assert.Equal("4", handler.Form["stars"]);
        Assert.Equal("Works well", handler.Form["comment"]);
        Assert.Equal("Ann B.", handler.Form["name"]);
        Assert.Equal("1", handler.Form["consent"]);
        Assert.Equal("1.1 (2)", handler.Form["app_version"]);
    }

    [Fact]
    public async Task Leaves_blank_message_and_name_empty()
    {
        var (client, handler) = Make((_, _) => Task.FromResult(Json(HttpStatusCode.OK, "{\"ok\":true,\"state\":\"updated\"}")));

        var outcome = await client.SendAsync(InstallId, "1.1 (2)", 5, null, "   ");

        Assert.Equal(RatingSendResult.Sent, outcome.Result);
        Assert.Equal("", handler.Form["comment"]);
        Assert.Equal("", handler.Form["name"]);
    }

    [Fact]
    public async Task Reports_an_already_moderated_rating_as_already_sent()
    {
        var (client, _) = Make((_, _) => Task.FromResult(Json(HttpStatusCode.OK, "{\"ok\":true,\"state\":\"exists\"}")));

        Assert.Equal(RatingSendResult.AlreadySent, (await client.SendAsync(InstallId, "1.1", 5, "x", null)).Result);
    }

    [Fact]
    public async Task Shows_the_servers_message_when_it_refuses_the_rating()
    {
        var (client, _) = Make((_, _) => Task.FromResult(Json((HttpStatusCode)429, "{\"ok\":false,\"errors\":[\"Too many ratings from here recently. Please try again later.\"]}")));

        var outcome = await client.SendAsync(InstallId, "1.1", 5, "x", null);

        Assert.Equal(RatingSendResult.Failed, outcome.Result);
        Assert.Equal("Too many ratings from here recently. Please try again later.", outcome.Message);
    }

    [Fact]
    public async Task Fails_politely_when_the_server_cannot_be_reached()
    {
        var (client, _) = Make((_, _) => throw new HttpRequestException("no route"));

        var outcome = await client.SendAsync(InstallId, "1.1", 5, "x", null);

        Assert.Equal(RatingSendResult.Failed, outcome.Result);
        Assert.Contains("connection", outcome.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Fails_politely_on_a_reply_that_is_not_json()
    {
        var (client, _) = Make((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("<html>challenge</html>") }));

        var outcome = await client.SendAsync(InstallId, "1.1", 5, "x", null);

        Assert.Equal(RatingSendResult.Failed, outcome.Result);
        Assert.False(string.IsNullOrWhiteSpace(outcome.Message));
    }

    [Fact]
    public async Task Rejects_stars_outside_1_to_5_without_calling_the_server()
    {
        var called = false;
        var (client, _) = Make((_, _) => { called = true; return Task.FromResult(Json(HttpStatusCode.OK, "{\"ok\":true}")); });

        Assert.Equal(RatingSendResult.Failed, (await client.SendAsync(InstallId, "1.1", 0, "x", null)).Result);
        Assert.Equal(RatingSendResult.Failed, (await client.SendAsync(InstallId, "1.1", 6, "x", null)).Result);
        Assert.False(called);
    }
}
