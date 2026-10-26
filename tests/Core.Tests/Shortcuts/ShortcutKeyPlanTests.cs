using IntegratedModManager.Core.Shortcuts;

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
    [InlineData("/", 0xBF)]
    [InlineData("[", 0xDB)]
    [InlineData("]", 0xDD)]
    [InlineData("=", 0xBB)]
    [InlineData("'", 0xDE)]
    [InlineData("\\", 0xDC)]
    [InlineData("`", 0xC0)]
    [InlineData("VK_CAPITAL", 0x14)]
    [InlineData("VK_OEM_102", 0xE2)]
    public void PunctuationResolvesLikeUsLayout(string expression, int expected)
    {
        Assert.True(ShortcutKeyPlan.TryCreate(expression, out var plan));
        Assert.Equal((ushort)expected, Assert.Single(plan!.Keys));
    }

    [Theory]
    [InlineData("no_modifiers /", new[] { 0xBF })]
    [InlineData("ctrl no_alt no_shift [", new[] { 0x11, 0xDB })]
    [InlineData("VK_RETURN VK_UP", new[] { 0x0D, 0x26 })]
    [InlineData("VK_BACK VK_DOWN", new[] { 0x08, 0x28 })]
    [InlineData("shift A B", new[] { 0x10, 0x41, 0x42 })]
    public void HeldKeysPrecedeTriggerInWrittenOrder(string expression, int[] expected)
    {
        Assert.True(ShortcutKeyPlan.TryCreate(expression, out var plan));
        Assert.Equal(expected.Select(k => (ushort)k), plan!.Keys);
    }

    [Fact]
    public void NoPrefixMayExcludeOrdinaryKeys()
    {
        Assert.True(ShortcutKeyPlan.TryCreate("NO_VK_RETURN NO_VK_BACK VK_UP", out var plan));
        Assert.Equal(new ushort[] { 0x26 }, plan!.Keys);
        Assert.Equal(new ushort[] { 0x0D, 0x08 }, plan.ForbiddenKeys);
    }

    [Theory]
    [InlineData("no_ctrl no_shift VK_LBUTTON", ShortcutUnsupportedReason.Mouse)]
    [InlineData("VK_XBUTTON1", ShortcutUnsupportedReason.Mouse)]
    [InlineData("ctrl alt VK_RBUTTON", ShortcutUnsupportedReason.Mouse)]
    [InlineData("XB_LEFT_THUMB XB_DPAD_UP", ShortcutUnsupportedReason.Controller)]
    [InlineData("NO_XB_LEFT_THUMB NO_XB_RIGHT_THUMB XB_DPAD_UP", ShortcutUnsupportedReason.Controller)]
    [InlineData("ctrl unknown", ShortcutUnsupportedReason.UnknownKey)]
    [InlineData("ctrl no_ctrl F6", ShortcutUnsupportedReason.Contradictory)]
    [InlineData("A B C D", ShortcutUnsupportedReason.TooManyKeys)]
    [InlineData("alt F4", ShortcutUnsupportedReason.SystemShortcut)]
    [InlineData("no_modifiers", ShortcutUnsupportedReason.Empty)]
    [InlineData("F6", ShortcutUnsupportedReason.None)]
    public void ExplainsWhyBindingCannotBeSent(string expression, ShortcutUnsupportedReason expected) =>
        Assert.Equal(expected, ShortcutKeyPlan.Explain(expression));

    [Fact]
    public void ScannerLabelOmitsAllExclusions()
    {
        var shortcut = Assert.Single(ModShortcutScanner.ParseText("[KeyMenuUp]\nkey = NO_VK_RETURN NO_VK_BACK VK_UP"));
        Assert.Equal("Up", shortcut.Shortcut);
        Assert.Equal("NO_VK_RETURN NO_VK_BACK VK_UP", shortcut.RawKey);
        Assert.Equal("Backspace+Up", Assert.Single(ModShortcutScanner.ParseText("[KeySliderUp]\nkey = VK_BACK VK_UP")).Shortcut);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no_modifiers")]
    [InlineData("ctrl no_ctrl F6")]
    [InlineData("no_ctrl RCONTROL F6")]
    [InlineData("no_modifiers alt F6")]
    [InlineData("ctrl XB_A")]
    [InlineData("alt VK_LBUTTON")]
    [InlineData("ctrl unknown")]
    [InlineData("A B C D")]
    [InlineData("VK_UP no_vk_up")]
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
