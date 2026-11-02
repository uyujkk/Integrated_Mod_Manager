using IntegratedModManager.Core.Layout;

namespace IntegratedModManager.Core.Tests;

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
