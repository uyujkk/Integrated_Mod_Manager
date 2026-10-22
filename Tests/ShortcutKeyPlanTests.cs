using IntegratedModManager.Core;

namespace IntegratedModManager.Core.Tests;

public sealed class ShortcutKeyPlanTests
{
    [Theory]
    [InlineData("VK_NUMPAD5", 0x65)]
    [InlineData("Num5", 0x65)]
    [InlineData("5", 0x35)]
    [InlineData("VK_MENU", 0x12)]
    [InlineData("VK_APPS", 0x5D)]
    [InlineData("VK_ADD", 0x6B)]
    [InlineData("VK_DIVIDE", 0x6F)]
    [InlineData("VK_OEM_PLUS", 0xBB)]
    [InlineData("+", 0xBB)]
    [InlineData("F24", 0x87)]
    public void DistinguishesPhysicalKeys(string expression, int expected)
    {
        Assert.True(ShortcutKeyPlan.TryCreate(expression, out var plan));
        Assert.Equal((ushort)expected, Assert.Single(plan!.Keys));
    }

    [Fact]
    public void ChordPreservesConstraintsAndOrdersModifiersBeforeKey()
    {
        Assert.True(ShortcutKeyPlan.TryCreate("no_ctrl VK_NUMPAD5 alt", out var plan));
        Assert.Equal(new ushort[] { 0x12, 0x65 }, plan!.Keys);
        Assert.Equal(new ushort[] { 0x11 }, plan.ForbiddenKeys);
    }

    [Theory]
    [InlineData("no_modifiers F6")]
    [InlineData("no_win no_shift CTRL F6")]
    [InlineData("VK_RCONTROL VK_NUMPAD5")]
    public void SupportsModifierConstraintsAndSidedKeys(string expression) => Assert.True(ShortcutKeyPlan.TryCreate(expression, out _));

    [Theory]
    [InlineData("")]
    [InlineData("no_modifiers")]
    [InlineData("ctrl no_ctrl F6")]
    [InlineData("no_ctrl RCONTROL F6")]
    [InlineData("no_modifiers alt F6")]
    [InlineData("ctrl XB_A")]
    [InlineData("alt VK_LBUTTON")]
    [InlineData("ctrl unknown")]
    [InlineData("A B")]
    [InlineData("F25")]
    [InlineData("Num10")]
    [InlineData("alt F4")]
    [InlineData("alt TAB")]
    [InlineData("ctrl alt delete")]
    [InlineData("WIN D")]
    public void UnsupportedOrContradictoryInputNeverProducesPartialChord(string expression)
    {
        Assert.False(ShortcutKeyPlan.TryCreate(expression, out var plan));
        Assert.Null(plan);
    }

    [Fact]
    public void ScannerRetainsRawModifiersAndCondition()
    {
        var shortcut = Assert.Single(ModShortcutScanner.ParseText("[KeyHelp]\nkey = no_ctrl VK_NUMPAD5\ncondition = $active == 1"));
        Assert.Equal("Num5", shortcut.Shortcut);
        Assert.Equal("no_ctrl VK_NUMPAD5", shortcut.RawKey);
        Assert.Equal("$active == 1", shortcut.Condition);
    }

    [Fact]
    public void ActiveScanSkipsDisabledIniAndDisabledSubdirectories()
    {
        string root = Path.Combine(Path.GetTempPath(), "imm-shortcut-scan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "DISABLED-old"));
        try
        {
            File.WriteAllText(Path.Combine(root, "mod.ini"), "[KeyLive]\nkey = F6");
            File.WriteAllText(Path.Combine(root, "DISABLED-mod.ini"), "[KeyOld]\nkey = F7");
            File.WriteAllText(Path.Combine(root, "DISABLED-old", "old.ini"), "[KeyHidden]\nkey = F8");
            Assert.Equal("F6", Assert.Single(ModShortcutScanner.ScanDirectory(root, activeOnly: true)).Shortcut);
            Assert.Equal(3, ModShortcutScanner.ScanDirectory(root).Count);
        }
        finally { Directory.Delete(root, true); }
    }
}
