using System.Net;
using System.Text;
using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Core.Tests.Services;

public class AssistantClientTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string json) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static (AssistantClient client, FakeHandler handler) Make(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new FakeHandler(respond);
        return (new AssistantClient(new HttpClient(handler), "https://example.test/assistant.php"), handler);
    }

    [Fact]
    public async Task An_ok_reply_is_the_answer_and_the_request_carries_question_and_excerpts()
    {
        var (client, handler) = Make(_ => Json(HttpStatusCode.OK, "{\"ok\":true,\"answer\":\"Long-press the chat.\"}"));

        var result = await client.AskAsync("install-id-1", "1.3 (4)", "How do I pin?", new[] { "Chats: pin it." });

        Assert.Equal(AssistantCloudStatus.Answered, result.Status);
        Assert.Equal("Long-press the chat.", result.Answer);
        Assert.Contains("\"question\":\"How do I pin?\"", handler.LastBody);
        Assert.Contains("Chats: pin it.", handler.LastBody);
        Assert.Contains("install-id-1", handler.LastBody);
    }

    [Fact]
    public async Task A_429_is_the_limit_status()
    {
        var (client, _) = Make(_ => Json((HttpStatusCode)429, "{\"ok\":false,\"error\":\"limit\"}"));

        Assert.Equal(AssistantCloudStatus.Limit, (await client.AskAsync("i", "v", "q?", [])).Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "{\"ok\":false,\"error\":\"unavailable\"}")]
    [InlineData(HttpStatusCode.OK, "not json")]
    [InlineData(HttpStatusCode.OK, "{\"ok\":true}")]
    public async Task Anything_else_is_unavailable(HttpStatusCode code, string body)
    {
        var (client, _) = Make(_ => Json(code, body));

        Assert.Equal(AssistantCloudStatus.Unavailable, (await client.AskAsync("i", "v", "q?", [])).Status);
    }

    [Fact]
    public async Task A_network_failure_is_unavailable()
    {
        var (client, _) = Make(_ => throw new HttpRequestException("offline"));

        Assert.Equal(AssistantCloudStatus.Unavailable, (await client.AskAsync("i", "v", "q?", [])).Status);
    }
}
