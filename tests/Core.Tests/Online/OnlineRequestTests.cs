using System.Net;
using System.Text;
using IntegratedModManager.Core.Online;

namespace IntegratedModManager.Core.Tests;

// Translation, images and download counts: the smaller requests the online page makes.
public sealed class OnlineRequestTests
{
    private static HttpClient Client(Func<HttpRequestMessage, int, HttpResponseMessage> reply)
    {
        int count = 0;
        return new HttpClient(new Handler(request => reply(request, count++)));
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Translate_JoinsTheSentencesOfTheReply()
    {
        Uri? asked = null;
        HttpClient client = Client((request, _) =>
        {
            asked = request.RequestUri;
            return Json("""[ [ ["Hello. ", "你好。"], ["World", "世界"], [null] ], null, "zh-CN" ]""");
        });

        Assert.Equal("Hello. World", await TextTranslationClient.TranslateAsync(client, "你好。世界", "en"));
        Assert.Contains("tl=en", asked!.Query);
        Assert.Contains("q=%E4%BD%A0%E5%A5%BD", asked.Query);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("[ [ [null] ] ]")]
    public async Task Translate_KeepsTheTextWhenTheReplyHasNone(string body)
        => Assert.Equal("原文", await TextTranslationClient.TranslateAsync(Client((_, _) => Json(body)), "原文", "en"));

    [Fact]
    public async Task TranslateLongText_TranslatesEachChunkAndKeepsParagraphBreaks()
    {
        string first = new('a', 2000), second = new('b', 2000);
        var sent = new List<string>();
        HttpClient client = Client((request, _) =>
        {
            string q = Uri.UnescapeDataString(request.RequestUri!.Query.Split("q=")[1]);
            sent.Add(q);
            return Json($$"""[ [ ["{{q.ToUpperInvariant()}} ", "x"] ] ]""");
        });

        string result = await TextTranslationClient.TranslateLongTextAsync(client, first + "\n\n" + second, "en");

        Assert.Equal([first, second], sent);
        Assert.Equal(first.ToUpperInvariant() + Environment.NewLine + Environment.NewLine + second.ToUpperInvariant(), result);
    }

    [Fact]
    public async Task TranslateLongText_SkipsBlankText()
        => Assert.Equal(string.Empty, await TextTranslationClient.TranslateLongTextAsync(Client((_, _) => throw new InvalidOperationException()), "  ", "en"));

    [Fact]
    public void SplitForTranslation_PacksParagraphsAndCutsLongOnes()
    {
        List<string> chunks = TextTranslationClient.SplitForTranslation("one\n\ntwo\r\n\r\n\n\nthree-long-paragraph", 10);

        Assert.Equal(["one" + Environment.NewLine + Environment.NewLine + "two", "three-long", "-paragraph"], chunks);
        Assert.Equal(["   "], TextTranslationClient.SplitForTranslation("   ", 10));
    }

    [Fact]
    public async Task ImageDownload_ReturnsTheBytes()
    {
        OnlineImageDownload download = await OnlineImageDownloader.DownloadAsync(
            Client((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) }), new Uri("https://img/x.png"), default);

        Assert.Equal([1, 2, 3], download.Bytes);
        Assert.Null(download.FailedStatus);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task ImageDownload_CountsTheLimitWhenNoLengthIsGiven(int overLimit, bool kept)
    {
        byte[] body = new byte[OnlineImageDownloader.MaxImageBytes + overLimit];
        // A stream that cannot seek has no length, like a chunked response.
        HttpClient client = Client((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new UnseekableStream(body)) });

        OnlineImageDownload download = await OnlineImageDownloader.DownloadAsync(client, new Uri("https://img/x.png"), default);

        Assert.Equal(kept ? body.Length : null, download.Bytes?.Length);
        Assert.Null(download.FailedStatus);
    }

    [Fact]
    public async Task ImageDownload_SkipsOversizedImages()
    {
        var content = new ByteArrayContent([1]);
        content.Headers.ContentLength = OnlineImageDownloader.MaxImageBytes + 1;

        OnlineImageDownload download = await OnlineImageDownloader.DownloadAsync(
            Client((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = content }), new Uri("https://img/x.png"), default);

        Assert.Equal(new OnlineImageDownload(null, null), download);
    }

    [Fact]
    public async Task ImageDownload_RetriesBusyServersAndWaitsAsAsked()
    {
        var waits = new List<TimeSpan>();
        HttpClient client = Client((_, attempt) =>
        {
            if (attempt == 2) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([7]) };
            var busy = new HttpResponseMessage(attempt == 0 ? HttpStatusCode.TooManyRequests : HttpStatusCode.BadGateway);
            if (attempt == 0) busy.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(2));
            return busy;
        });

        OnlineImageDownload download = await OnlineImageDownloader.DownloadAsync(client, new Uri("https://img/x.png"), default,
            (delay, _) => { waits.Add(delay); return Task.CompletedTask; });

        Assert.Equal([7], download.Bytes);
        Assert.Equal([TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(1200)], waits);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, 1)]
    [InlineData(HttpStatusCode.ServiceUnavailable, OnlineImageDownloader.MaxAttempts)]
    public async Task ImageDownload_GivesUpWithTheLastStatus(HttpStatusCode status, int expectedAttempts)
    {
        int attempts = 0;
        HttpClient client = Client((_, _) => { attempts++; return new HttpResponseMessage(status); });

        OnlineImageDownload download = await OnlineImageDownloader.DownloadAsync(client, new Uri("https://img/x.png"), default,
            (_, _) => Task.CompletedTask);

        Assert.Equal(new OnlineImageDownload(null, status), download);
        Assert.Equal(expectedAttempts, attempts);
    }

    [Theory]
    [InlineData(null, 0, 600)]
    [InlineData(null, 1, 1200)]
    [InlineData(null, 5, 6000)]
    [InlineData(30, 0, 6000)]
    public void ImageRetryDelay_GrowsAndIsCapped(int? retryAfterSeconds, int attempt, int expectedMilliseconds)
        => Assert.Equal(TimeSpan.FromMilliseconds(expectedMilliseconds),
            OnlineImageDownloader.RetryDelay(retryAfterSeconds is int seconds ? TimeSpan.FromSeconds(seconds) : null, attempt));

    [Fact]
    public async Task DownloadCounts_BatchesDistinctIds()
    {
        var batches = new List<int>();
        HttpClient client = Client((request, _) =>
        {
            int ids = request.RequestUri!.Query.Split("itemid%5B%5D=").Length - 1;
            batches.Add(ids);
            return Json("[" + string.Join(",", Enumerable.Repeat("""{ "downloads": 5 }""", ids)) + "]");
        });
        int[] items = [.. Enumerable.Range(1, GameBananaDownloadCounts.MaxBatch + 5), 1, 2];

        Dictionary<int, int> counts = await GameBananaDownloadCounts.FetchAsync(client, items);

        Assert.Equal([GameBananaDownloadCounts.MaxBatch, 5], batches);
        Assert.Equal(GameBananaDownloadCounts.MaxBatch + 5, counts.Count);
        Assert.All(counts.Values, value => Assert.Equal(5, value));
    }

    [Fact]
    public async Task DownloadCounts_ReportTheRateLimit()
    {
        HttpClient client = Client((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return response;
        });

        OnlineRateLimitException limit = await Assert.ThrowsAsync<OnlineRateLimitException>(() => GameBananaDownloadCounts.FetchAsync(client, [1]));
        Assert.Equal(TimeSpan.FromSeconds(30), limit.RetryAfter);
    }

    private sealed class UnseekableStream(byte[] body) : MemoryStream(body)
    {
        public override bool CanSeek => false;
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(reply(request));
    }
}
