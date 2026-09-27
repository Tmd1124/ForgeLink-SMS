using System.Net;
using System.Text;
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Tests.Services;

public class LinkPreviewServiceTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Html(string html) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(html, Encoding.UTF8, "text/html")
    };

    private const string RecipePage = """
        <html><head>
          <title>Fallback title</title>
          <meta property="og:title" content="Grandma&#39;s Apple Pie" />
          <meta content="The best pie you'll ever bake." property="og:description">
          <meta property="og:image" content="/images/pie.jpg">
          <meta property="og:site_name" content="Allrecipes">
        </head><body>…</body></html>
        """;

    [Fact]
    public void OpenGraphParser_reads_title_description_image_and_site()
    {
        var preview = OpenGraphParser.Parse("https://www.allrecipes.com/recipe/12682", RecipePage);

        Assert.NotNull(preview);
        Assert.Equal("Grandma's Apple Pie", preview.Title);
        Assert.Equal("The best pie you'll ever bake.", preview.Description);
        Assert.Equal("https://www.allrecipes.com/images/pie.jpg", preview.ImageUrl);
        Assert.Equal("Allrecipes", preview.SiteName);
    }

    [Fact]
    public void OpenGraphParser_falls_back_to_the_page_title_and_domain()
    {
        var preview = OpenGraphParser.Parse("https://www.weather.gov/ffc", "<html><head><title> Atlanta Forecast </title></head></html>");

        Assert.NotNull(preview);
        Assert.Equal("Atlanta Forecast", preview.Title);
        Assert.Null(preview.ImageUrl);
        Assert.Equal("weather.gov", preview.SiteName);
    }

    [Fact]
    public void OpenGraphParser_returns_null_for_a_page_without_a_title()
    {
        Assert.Null(OpenGraphParser.Parse("https://example.com", "<html><body>hi</body></html>"));
    }

    [Fact]
    public async Task GetAsync_fetches_each_link_only_once()
    {
        var handler = new FakeHandler(_ => Html(RecipePage));
        var service = new LinkPreviewService(new HttpClient(handler));

        var first = await service.GetAsync("https://www.allrecipes.com/recipe/12682");
        var second = await service.GetAsync("https://www.allrecipes.com/recipe/12682");

        Assert.Equal("Grandma's Apple Pie", first?.Title);
        Assert.Same(first, second);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task GetAsync_returns_null_for_non_pages_and_errors()
    {
        var service = new LinkPreviewService(new HttpClient(new FakeHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/photo.jpg" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1, 2 }) { Headers = { ContentType = new("image/jpeg") } } },
            "/missing" => new HttpResponseMessage(HttpStatusCode.NotFound),
            _ => throw new HttpRequestException("offline")
        })));

        Assert.Null(await service.GetAsync("https://example.com/photo.jpg"));
        Assert.Null(await service.GetAsync("https://example.com/missing"));
        Assert.Null(await service.GetAsync("https://example.com/offline"));
    }

    private sealed class PlatformException(string message) : Exception(message);

    [Fact]
    public async Task GetAsync_returns_null_when_the_platform_handler_throws_its_own_error()
    {
        // Android's handler throws Java.Net.MalformedURLException ("unknown protocol: intent")
        // when a web link redirects to an app link.
        var service = new LinkPreviewService(new HttpClient(new FakeHandler(_ => throw new PlatformException("unknown protocol: intent"))));

        Assert.Null(await service.GetAsync("https://photos.example.com/share/abc"));
    }
}
