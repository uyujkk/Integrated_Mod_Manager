using IntegratedModManager.Core.Presets;

namespace IntegratedModManager.Core.Tests;

// Names come from real Mods in a maintainer's repository.
public sealed class TransientVariablePolicyTests
{
    [Theory]
    [InlineData("$menu")]
    [InlineData("$gui")]
    [InlineData("$page")]
    [InlineData("$current_page_number")]
    [InlineData("$firstload")]
    [InlineData("$menu_orientation")]
    [InlineData("$menuRatio")]
    [InlineData("$select_loc_col")]
    [InlineData("$MenuLocX")]
    [InlineData("$helpmenuwidthy")]
    [InlineData("$NewMenuLocY")]
    [InlineData("$menuopeniconlocx")]
    [InlineData("$pageiconspacingx")]
    [InlineData("$panel1offsety")]
    [InlineData("$paneldecaloffsetx")]
    [InlineData("$clearpagewidthx")]
    [InlineData("$randomizeautolocy")]
    public void MenuStateIsTransient(string variable) => Assert.True(TransientVariablePolicy.IsTransient(variable));

    [Theory]
    [InlineData("$swapvar")]
    [InlineData("$ShapeKey_10")]
    [InlineData("$Freq_Arm_Band")]
    [InlineData("$fade")]
    [InlineData("$selectedpreset")]
    [InlineData("$auto_randomize_preset")]
    [InlineData("$decalscale")]
    [InlineData("$glowoffset")]
    [InlineData("$schooltimecolorshiftcolorwheellocx")]
    [InlineData("$cowgirltrimdiffusehue")]
    [InlineData("$insertanimspeed")]
    [InlineData("$bust_boost_percent")]
    [InlineData("$page_style")]
    [InlineData("$slider")]
    public void LookSettingsAreKept(string variable) => Assert.False(TransientVariablePolicy.IsTransient(variable));

    [Fact]
    public void Split_SeparatesMenuStateButNeverEmptiesASlot()
    {
        ModPersistentValue look = new("mod.ini", "$swapvar", "$\\mods\\a\\mod.ini\\swapvar", "1");
        ModPersistentValue menu = new("mod.ini", "$menu", "$\\mods\\a\\mod.ini\\menu", "1");

        var (kept, transient) = TransientVariablePolicy.Split([look, menu]);
        Assert.Equal([look], kept);
        Assert.Equal([menu], transient);

        var (onlyMenu, none) = TransientVariablePolicy.Split([menu]);
        Assert.Equal([menu], onlyMenu);
        Assert.Empty(none);
    }
}
