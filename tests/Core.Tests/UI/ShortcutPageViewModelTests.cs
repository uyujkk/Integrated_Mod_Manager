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
        var page = new ShortcutPageViewModel { SelectedTarget = Target(1) };
        var changed = new List<string?>();
        page.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        page.InputEnabled = true;
        page.SendInProgress = true;
        page.OverlayOpacity = 50;

        Assert.Equal([nameof(ShortcutPageViewModel.InputEnabled), nameof(ShortcutPageViewModel.SendInProgress), nameof(ShortcutPageViewModel.OverlayOpacity)], changed);
    }

    private static ShortcutTarget Target(nint handle) => new(handle, 100, 1, "Game");

    [Fact]
    public void InputEnabled_NeedsATargetWindow()
    {
        var page = new ShortcutPageViewModel();

        page.InputEnabled = true;
        Assert.False(page.InputEnabled);

        page.SelectedTarget = Target(1);
        page.InputEnabled = true;
        Assert.True(page.InputEnabled);
    }

    [Fact]
    public void SelectedTarget_ChangingItDisarmsInput()
    {
        var page = new ShortcutPageViewModel { SelectedTarget = Target(1) };
        page.InputEnabled = true;

        page.SelectedTarget = Target(2);

        Assert.False(page.InputEnabled);
    }

    [Theory]
    [InlineData(double.NaN, double.NaN, 200, 300)]
    [InlineData(150d, 900d, 150, 900)]
    public void Durations_FallBackToTheDefaultsWhenCleared(double tap, double hold, int tapMs, int holdMs)
    {
        var page = new ShortcutPageViewModel { TapDuration = tap, HoldDuration = hold };
        Assert.Equal((tapMs, holdMs), (page.TapMilliseconds, page.HoldMilliseconds));
    }

    [Fact]
    public void Request_ReachesTheWindow()
    {
        var page = new ShortcutPageViewModel();
        ShortcutPageRequest? seen = null;
        page.ActionRequested += (_, request) => seen = request;

        page.Request(ShortcutPageAction.SetHotkey, (3u, 0x4Bu));

        Assert.Equal(new ShortcutPageRequest(ShortcutPageAction.SetHotkey, (3u, 0x4Bu)), seen);
    }
}
