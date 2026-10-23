using IntegratedModManager.Core.Online;

namespace IntegratedModManager.Core.Tests;

public sealed class GameBananaDownloadCountsTests
{
    [Fact]
    public void BatchUrlRequestsEveryItemInOrder()
    {
        string url = GameBananaDownloadCounts.BatchUrl([677818, 654357]);
        Assert.StartsWith("https://api.gamebanana.com/Core/Item/Data?", url);
        Assert.True(url.IndexOf("itemid%5B%5D=677818", StringComparison.Ordinal) < url.IndexOf("itemid%5B%5D=654357", StringComparison.Ordinal));
        Assert.Equal(2, url.Split("fields%5B%5D=downloads").Length - 1);
        Assert.EndsWith("return_keys=true&format=json_min", url);
    }

    [Fact]
    public void BatchUrlRejectsEmptyOversizedOrInvalidRequests()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GameBananaDownloadCounts.BatchUrl([]));
        Assert.Throws<ArgumentOutOfRangeException>(() => GameBananaDownloadCounts.BatchUrl(Enumerable.Range(1, 41).ToArray()));
        Assert.Throws<ArgumentOutOfRangeException>(() => GameBananaDownloadCounts.BatchUrl([5, 0]));
        Assert.Equal(40, GameBananaDownloadCounts.BatchUrl(Enumerable.Range(1, 40).ToArray()).Split("itemid%5B%5D=").Length - 1);
    }

    [Fact]
    public void ParseMapsByPositionAndSkipsPerItemErrors()
    {
        // Shape observed from the live API, including an error entry for a missing item.
        const string json = """[{"downloads":4468},{"error":"[itemid] `999999999` doesn't exist","error_code":"INVALID_PARAMS"},{"downloads":518}]""";
        var counts = GameBananaDownloadCounts.Parse(json, [10, 999999999, 30]);
        Assert.Equal(2, counts.Count);
        Assert.Equal(4468, counts[10]);
        Assert.Equal(518, counts[30]);
        Assert.False(counts.ContainsKey(999999999));
    }

    [Fact]
    public void ParseKeepsGenuineZeroAndClampsNegative()
    {
        var counts = GameBananaDownloadCounts.Parse("""[{"downloads":0},{"downloads":-4}]""", [1, 2]);
        Assert.Equal(0, counts[1]);
        Assert.Equal(0, counts[2]);
    }

    [Theory]
    [InlineData("""[{"downloads":1}]""")]
    [InlineData("""{"downloads":1}""")]
    [InlineData("""[{"downloads":1},{"downloads":2},{"downloads":3}]""")]
    public void ParseRejectsResponsesThatCannotBeMatchedToTheRequest(string json) =>
        Assert.Throws<InvalidDataException>(() => GameBananaDownloadCounts.Parse(json, [1, 2]));
}
