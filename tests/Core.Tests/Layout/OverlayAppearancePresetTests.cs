using IntegratedModManager.Core.Layout;

namespace IntegratedModManager.Core.Tests;

public sealed class OverlayAppearancePresetTests
{
    [Fact]
    public void All_GetMoreTransparentInOrder()
    {
        var presets = OverlayAppearancePreset.All;

        Assert.Equal(["clear", "balanced", "minimal"], presets.Select(preset => preset.Id));
        Assert.True(presets[0].BackgroundOpacity > presets[1].BackgroundOpacity && presets[1].BackgroundOpacity > presets[2].BackgroundOpacity);
    }

    [Fact]
    public void All_StayInsideWhatTheSlidersAllow()
    {
        Assert.All(OverlayAppearancePreset.All, preset =>
        {
            Assert.InRange(preset.BackgroundOpacity, 10, 100);
            Assert.Equal(preset.ElementOpacity, OverlayAppearancePolicy.ClampElementOpacity(preset.ElementOpacity));
        });
    }

    [Fact]
    public void Minimal_StillGetsTheTextShadow()
        => Assert.True(OverlayAppearancePolicy.ShowTextShadow(OverlayAppearancePreset.All[2].BackgroundOpacity));
}
