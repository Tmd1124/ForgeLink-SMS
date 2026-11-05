using System.Net;
using System.Text;
using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Core.Tests.Services;

public class AssistantServiceTests
{
    private sealed class FakeHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(respond());
        }
    }

    private static HttpResponseMessage Answer(string text) =>
        new(HttpStatusCode.OK) { Content = new StringContent($"{{\"ok\":true,\"answer\":\"{text}\"}}", Encoding.UTF8, "application/json") };

    private static (AssistantService service, FakeHandler handler) Make(Func<HttpResponseMessage> respond)
    {
        var handler = new FakeHandler(respond);
        var client = new AssistantClient(new HttpClient(handler), "https://example.test/assistant.php");
        return (new AssistantService(client, () => "install-1", "1.3 (4)"), handler);
    }

    [Fact]
    public async Task Price_questions_are_answered_with_fixed_text_and_never_leave_the_phone()
    {
        var (service, handler) = Make(() => Answer("model"));

        var reply = await service.AskAsync("How much is it?");

        Assert.Equal(AssistantSource.Fixed, reply.Source);
        Assert.Contains("$4.99", reply.Text);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task A_help_question_uses_the_cloud_answer_and_names_the_topic()
    {
        var (service, handler) = Make(() => Answer("Long-press the chat and tap pin."));

        var reply = await service.AskAsync("How do I pin a conversation?");

        Assert.Equal(AssistantSource.Cloud, reply.Source);
        Assert.Equal("Long-press the chat and tap pin.", reply.Text);
        Assert.Equal("Chats", reply.Topic);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData((HttpStatusCode)429)]
    public async Task When_the_cloud_fails_the_best_help_topic_is_shown_instead(HttpStatusCode code)
    {
        var (service, _) = Make(() => new HttpResponseMessage(code) { Content = new StringContent("{}") });

        var reply = await service.AskAsync("How do I pin a conversation?");

        Assert.Equal(AssistantSource.Offline, reply.Source);
        Assert.Equal("Chats", reply.Topic);
        Assert.Contains("pin", reply.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_question_the_help_does_not_cover_skips_the_cloud_and_points_to_a_report()
    {
        var (service, handler) = Make(() => Answer("model"));

        var reply = await service.AskAsync("zzzz qqqq xxxx");

        Assert.Equal(AssistantSource.Offline, reply.Source);
        Assert.Null(reply.Topic);
        Assert.Contains("Send a report", reply.Text);
        Assert.Equal(0, handler.Calls);
    }
}
