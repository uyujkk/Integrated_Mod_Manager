using IntegratedModManager.Core.Online;

namespace IntegratedModManager.Core.Tests;

public sealed class CharacterImageMatcherTests
{
    private static byte[] Pattern(bool inverse = false) => Enumerable.Range(0, CharacterImageMatcher.FeatureBytes)
        .Select(i => (byte)(((i / 3 % 2 == 0) ^ inverse) ? 30 : 230)).ToArray();

    [Fact]
    public void SwitchRequiresThreeConsecutiveReliableObservations()
    {
        var matcher = new CharacterImageMatcher();
        var templates = new Dictionary<string, byte[]> { ["A"] = Pattern(), ["B"] = Pattern(true) };
        Assert.Null(matcher.Observe(Pattern(), templates, out _));
        Assert.Null(matcher.Observe(Pattern(), templates, out _));
        Assert.Equal("A", matcher.Observe(Pattern(), templates, out double confidence));
        Assert.Equal(1d, confidence);
        Assert.Null(matcher.Observe(Pattern(true), templates, out _));
        Assert.Null(matcher.Observe(Pattern(true), templates, out _));
        Assert.Equal("B", matcher.Observe(Pattern(true), templates, out _));
    }

    [Fact]
    public void AmbiguousOrBlankFramesResetConfirmation()
    {
        var matcher = new CharacterImageMatcher();
        var templates = new Dictionary<string, byte[]> { ["A"] = Pattern(), ["B"] = Pattern() };
        for (int i = 0; i < 4; i++) Assert.Null(matcher.Observe(Pattern(), templates, out _));
        templates.Remove("B");
        Assert.Null(matcher.Observe(Pattern(), templates, out _));
        Assert.Null(matcher.Observe(new byte[CharacterImageMatcher.FeatureBytes], templates, out _));
        Assert.Null(matcher.Observe(Pattern(), templates, out _));
        Assert.Null(matcher.Observe(Pattern(), templates, out _));
        Assert.Equal("A", matcher.Observe(Pattern(), templates, out _));
    }

    [Fact]
    public void SceneWithNoMatchNeverReturnsACharacter()
    {
        var matcher = new CharacterImageMatcher();
        var templates = new Dictionary<string, byte[]> { ["A"] = Pattern() };
        for (int i = 0; i < 5; i++) Assert.Null(matcher.Observe(Pattern(true), templates, out _));
    }

    [Fact]
    public void FlatColorIsNotAUsableReference()
    {
        byte[] red = Enumerable.Range(0, CharacterImageMatcher.FeatureBytes).Select(i => (byte)(i % 3 == 2 ? 255 : 0)).ToArray();
        Assert.False(CharacterImageMatcher.HasDetail(red));
    }

    [Fact]
    public void RegionExtractionIgnoresPixelsOutsideSelectedHud()
    {
        var image = new byte[48 * 24 * 4];
        byte[] pattern = Pattern();
        for (int y = 0; y < 24; y++) for (int x = 0; x < 24; x++) for (int c = 0; c < 3; c++)
            image[(y * 48 + x + 24) * 4 + c] = pattern[(y * 24 + x) * 3 + c];
        Assert.Equal(pattern, CharacterImageMatcher.Extract(image, 48, 24, new(.5, 0, .5, 1)));
    }

    [Theory]
    [InlineData(-.1, 0, .5, .5)]
    [InlineData(0, 0, 0, .5)]
    [InlineData(.8, 0, .5, .5)]
    [InlineData(0, double.NaN, .5, .5)]
    public void InvalidCropIsRejected(double x, double y, double w, double h)
    {
        var crop = new CharacterScreenRegion(x, y, w, h);
        Assert.False(crop.IsValid);
        Assert.Throws<ArgumentException>(() => CharacterImageMatcher.Extract(new byte[24 * 24 * 4], 24, 24, crop));
    }
}
