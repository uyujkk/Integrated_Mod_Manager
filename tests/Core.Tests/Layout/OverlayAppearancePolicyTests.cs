using IntegratedModManager.Core.Layout;

namespace IntegratedModManager.Core.Tests;

public sealed class OverlayAutoContrastTests
{
    [Fact]
    public void AverageLuminance_WeighsChannelsLikeTheEye()
    {
        byte[] white = Enumerable.Repeat((byte)255, 64 * 4).ToArray();
        byte[] green = new byte[64 * 4];
        for (int i = 1; i < green.Length; i += 4) green[i] = 255;

        Assert.Equal(255, IntegratedModManager.Core.Layout.OverlayAppearancePolicy.AverageLuminance(white), 1);
        Assert.Equal(0.587 * 255, IntegratedModManager.Core.Layout.OverlayAppearancePolicy.AverageLuminance(green), 1);
        Assert.Equal(0, IntegratedModManager.Core.Layout.OverlayAppearancePolicy.AverageLuminance([]));
    }

    [Theory]
    [InlineData(200, false, true)]
    [InlineData(140, false, false)]
    [InlineData(140, true, true)]
    [InlineData(90, true, false)]
    public void PrefersDarkText_HasAGapSoTheTextDoesNotFlicker(double luminance, bool darkNow, bool expected)
        => Assert.Equal(expected, IntegratedModManager.Core.Layout.OverlayAppearancePolicy.PrefersDarkText(luminance, darkNow));
}

public sealed class OverlayAppearancePolicyTests
{
    [Theory]
    [InlineData(null, 70)]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(45, 45)]
    [InlineData(250, 100)]
    public void ClampElementOpacity_DefaultsMissingValuesAndBoundsTheRest(int? saved, int expected) =>
        Assert.Equal(expected, OverlayAppearancePolicy.ClampElementOpacity(saved));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(40, 102)]
    [InlineData(100, 255)]
    public void ElementAlpha_MapsPercentToAlpha(int percent, byte expected) =>
        Assert.Equal(expected, OverlayAppearancePolicy.ElementAlpha(percent));

    [Fact]
    public void HoverAlpha_StaysVisibleWithoutBackingAndNeverDropsBelowIt()
    {
        Assert.True(OverlayAppearancePolicy.HoverAlpha(0) > 0);
        Assert.Equal(255, OverlayAppearancePolicy.HoverAlpha(100));
    }

    [Theory]
    [InlineData(10, true)]
    [InlineData(59, true)]
    [InlineData(60, false)]
    [InlineData(100, false)]
    public void ShowTextShadow_OnlyBelowSixtyPercentBackground(int background, bool expected) =>
        Assert.Equal(expected, OverlayAppearancePolicy.ShowTextShadow(background));

    [Theory]
    [InlineData(255, 255, 255, false)]
    [InlineData(255, 232, 163, false)]
    [InlineData(32, 32, 32, true)]
    public void UsesLightBacking_OnlyForDarkText(byte red, byte green, byte blue, bool expected) =>
        Assert.Equal(expected, OverlayAppearancePolicy.UsesLightBacking(red, green, blue));
}
