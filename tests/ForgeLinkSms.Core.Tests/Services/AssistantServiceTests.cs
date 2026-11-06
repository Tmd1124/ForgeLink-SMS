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

    private static HttpResponseMessage WebAnswer(string text) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent($"{{\"ok\":true,\"answer\":\"{text}\",\"web\":true,\"sources\":[{{\"title\":\"Recap\",\"url\":\"https://example.com/recap\"}}]}}", Encoding.UTF8, "application/json")
        };

    [Fact]
    public async Task With_web_on_a_general_question_is_answered_from_the_web_with_its_sources()
    {
        var (service, handler) = Make(() => WebAnswer("It was 3-2."));

        var reply = await service.AskAsync("Who won the game last night?", web: true);

        Assert.Equal(AssistantSource.Web, reply.Source);
        Assert.Equal("It was 3-2.", reply.Text);
        Assert.Equal("https://example.com/recap", Assert.Single(reply.Sources!).Url);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task With_web_off_nothing_is_searched()
    {
        var (service, handler) = Make(() => Answer("Long-press the chat and tap pin."));

        var reply = await service.AskAsync("How do I pin a conversation?", web: false);

        Assert.NotEqual(AssistantSource.Web, reply.Source);
        Assert.Equal(1, handler.Calls);
        Assert.Null(reply.Sources);
    }

    [Fact]
    public async Task Fixed_answers_win_even_with_web_on_and_never_leave_the_phone()
    {
        var (service, handler) = Make(() => WebAnswer("web"));

        var reply = await service.AskAsync("How much is it?", web: true);

        Assert.Equal(AssistantSource.Fixed, reply.Source);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task With_googling_on_a_question_about_the_app_still_uses_the_help_pages_and_is_not_searched()
    {
        var (service, handler) = Make(() => Answer("Long-press the chat and tap pin."));

        var reply = await service.AskAsync("How do I pin a conversation?", web: true);

        Assert.Equal(AssistantSource.Cloud, reply.Source);
        Assert.Equal("Long-press the chat and tap pin.", reply.Text);
        Assert.Null(reply.Sources);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task When_googling_is_unavailable_a_general_question_says_so_and_points_to_the_help()
    {
        var (service, handler) = Make(() => new HttpResponseMessage((HttpStatusCode)429));

        var reply = await service.AskAsync("Who won the game last night?", web: true);

        Assert.Equal(AssistantSource.Offline, reply.Source);
        Assert.StartsWith(AssistantService.WebUnavailableNote, reply.Text);
        Assert.Contains("couldn't find that in the ForgeLink help", reply.Text);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task When_googling_is_off_a_general_question_is_never_searched()
    {
        var (service, _) = Make(() => WebAnswer("web"));

        var reply = await service.AskAsync("Who won the game last night?", web: false);

        Assert.NotEqual(AssistantSource.Web, reply.Source);
        Assert.Null(reply.Sources);
    }
}
