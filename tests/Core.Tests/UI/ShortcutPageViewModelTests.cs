using IntegratedModManager.Core.Shortcuts;
using ModFolderCopier.WinUI;

namespace IntegratedModManager.Core.Tests;

public sealed class ShortcutPageViewModelTests
{
    private static DetectedModShortcut Binding(string key) => new(key, "TestSection", "Behavior", "Target", "mod.ini");

    [Fact]
    public void LoadSettings_KeepsValidValues()
    {
        var page = new ShortcutPageViewModel();

        page.LoadSettings("VirtualKey", 40, true, false, 6, 0x78);

        Assert.Equal((ShortcutInputMode.VirtualKey, 40, true, false, 6u, 0x78u),
            (page.InputMode, page.OverlayOpacity, page.HideUnsupported, page.HotkeyEnabled, page.HotkeyModifiers, page.HotkeyKey));
    }

    [Fact]
    public void LoadSettings_FallsBackForMissingOrDamagedValues()
    {
        var page = new ShortcutPageViewModel();

        page.LoadSettings("Bogus", null, null, true, 9, 0x20);

        Assert.Equal((ShortcutInputMode.ScanCode, 100, false, ShortcutPageViewModel.DefaultHotkeyModifiers, ShortcutPageViewModel.DefaultHotkeyKey),
            (page.InputMode, page.OverlayOpacity, page.HideUnsupported, page.HotkeyModifiers, page.HotkeyKey));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(55, 55)]
    [InlineData(250, 100)]
    public void OverlayOpacity_StaysInRange(int value, int expected) => Assert.Equal(expected, new ShortcutPageViewModel { OverlayOpacity = value }.OverlayOpacity);

    [Fact]
    public void InputMode_RejectsUndefinedValues() => Assert.Equal(ShortcutInputMode.ScanCode, new ShortcutPageViewModel { InputMode = (ShortcutInputMode)42 }.InputMode);

    [Theory]
    [InlineData(3u, 0x4Bu, "Ctrl+Alt", "K")]
    [InlineData(5u, 0x70u, "Alt+Shift", "F1")]
    [InlineData(6u, 0x7Bu, "Ctrl+Shift", "F12")]
    public void HotkeyLabel_ReadsLikeTheKeys(uint modifiers, uint key, string expectedModifiers, string expectedKey)
        => Assert.Equal((expectedModifiers, expectedKey), new ShortcutPageViewModel { HotkeyModifiers = modifiers, HotkeyKey = key }.HotkeyLabel());

    [Fact]
    public void VisibleShortcuts_HidesOnlyUnsendableKeysWhenAsked()
    {
        var page = new ShortcutPageViewModel { Detected = [Binding("h"), Binding("NO_SUCH_KEY_AT_ALL")] };

        Assert.Equal(2, page.VisibleShortcuts().Length);
        page.HideUnsupported = true;
        Assert.Equal("h", Assert.Single(page.VisibleShortcuts()).RawKey);
    }

    [Fact]
    public void Properties_RaiseChangeNotifications()
    {
        var page = new ShortcutPageViewModel();
        var changed = new List<string?>();
        page.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        page.InputEnabled = true;
        page.SendInProgress = true;
        page.OverlayOpacity = 50;

        Assert.Equal([nameof(ShortcutPageViewModel.InputEnabled), nameof(ShortcutPageViewModel.SendInProgress), nameof(ShortcutPageViewModel.OverlayOpacity)], changed);
    }
}
