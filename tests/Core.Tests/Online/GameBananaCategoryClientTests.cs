using System.Net;
using System.Text;
using IntegratedModManager.Core.Online;

namespace IntegratedModManager.Core.Tests;

public sealed class GameBananaCategoryClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);

    private static (HttpClient Client, List<Uri> Requests) Client(HttpStatusCode status, string body, TimeSpan? retryAfter = null)
    {
        var requests = new List<Uri>();
        var client = new HttpClient(new Handler(request =>
        {
            requests.Add(request.RequestUri!);
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            if (retryAfter is { } delay) response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(delay);
            return response;
        }));
        return (client, requests);
    }

    [Fact]
    public async Task GetPage_ParsesRecordsTotalsAndPageSize()
    {
        var (client, requests) = Client(HttpStatusCode.OK, """
            { "_aMetadata": { "_nRecordCount": 41, "_nPerpage": 20 },
              "_aRecords": [ { "_idRow": 123, "_sName": "Skin", "_aSubmitter": { "_sName": "Author", "_idRow": 45 } }, { "_sName": "no id" } ] }
            """);

        OnlineCategoryPageResult page = await GameBananaCategoryClient.GetPageAsync(client, "42770", 2, 20, 0, Now);

        Assert.Equal(123, Assert.Single(page.Mods).ItemId);
        Assert.Equal("Author", page.Mods[0].Author);
        Assert.Equal((41, 2, 20, 3), (page.TotalCount, page.Page, page.PageSize, page.TotalPages));
        Assert.Contains("42770", Assert.Single(requests).ToString());
    }

    [Fact]
    public async Task GetPage_ClampsThePageSize()
    {
        var (client, requests) = Client(HttpStatusCode.OK, "{}");

        await GameBananaCategoryClient.GetPageAsync(client, "1", 1, 500, 0, Now);

        Assert.Equal(OnlineBrowsePolicy.CategoryPageUrl("1", 1, 50, 0), requests[0].ToString());
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{ \"_aMetadata\": { \"_nRecordCount\": 3 } }")]
    public async Task GetPage_ToleratesMissingRecords(string body)
    {
        var (client, _) = Client(HttpStatusCode.OK, body);

        Assert.Empty((await GameBananaCategoryClient.GetPageAsync(client, "1", 1, 20, 0, Now)).Mods);
    }

    [Fact]
    public async Task GetPage_ReportsRateLimitsWithTheirDelay()
    {
        var (client, _) = Client(HttpStatusCode.TooManyRequests, "{}", TimeSpan.FromSeconds(30));

        var error = await Assert.ThrowsAsync<OnlineRateLimitException>(() => GameBananaCategoryClient.GetPageAsync(client, "1", 1, 20, 0, Now));

        Assert.Equal(TimeSpan.FromSeconds(30), error.RetryAfter);
    }

    [Fact]
    public async Task GetPage_FailsOnOtherErrors()
    {
        var (client, _) = Client(HttpStatusCode.InternalServerError, "{}");

        await Assert.ThrowsAsync<HttpRequestException>(() => GameBananaCategoryClient.GetPageAsync(client, "1", 1, 20, 0, Now));
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(reply(request));
    }
}
