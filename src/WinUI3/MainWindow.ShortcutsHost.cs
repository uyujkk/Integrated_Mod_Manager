using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

// Stage J5a: the window's side of the Shortcuts page. The page (Pages/ShortcutsPage) shows ShortcutPageViewModel and
// asks for work through it; the overlay is reached through IShortcutOverlay. Until J5b the window still owns the
// overlay's content and the settings blocks it builds for the page (IShortcutPageSettings).
public sealed partial class MainWindow : IShortcutOverlay, IShortcutPageSettings
{
    private void AttachShortcutServices()
    {
        _services.Overlay = this;
        _services.ShortcutSettings = this;
        _shortcuts.ActionRequested += OnShortcutPageRequested;
    }

    // After the settings are loaded, so loading them is not taken for the user's changes.
    private void ListenToShortcutState() => _shortcuts.PropertyChanged += OnShortcutStateChanged;

    bool IShortcutOverlay.IsVisible => _shortcuts.OverlayVisible;

    bool IShortcutOverlay.IsClickThrough => _overlayClickThrough;

    void IShortcutOverlay.Open() => OpenShortcutOverlay();

    void IShortcutOverlay.Hide()
    {
        _shortcutOverlay?.AppWindow.Hide();
        _shortcuts.OverlayVisible = false;
    }

    void IShortcutOverlay.Toggle() => ToggleShortcutOverlay();

    void IShortcutOverlay.Refresh(bool bindingsChanged) => RefreshShortcutOverlay(bindingsChanged);

    void IShortcutPageSettings.AddOverlayAppearance(StackPanel group, Slider backgroundOpacity)
    {
        group.Children.Add(CreateOverlayAppearancePresetRow());
        AddOverlayAutoContrastSetting(group);
        group.Children.Add(backgroundOpacity);
        _overlayElementSlider = CreateOverlayElementOpacitySlider();
        group.Children.Add(_overlayElementSlider);
        group.Children.Add(BuildOverlayTextColorButton());
    }

    void IShortcutPageSettings.AddOverlayBehavior(StackPanel group)
    {
        AddOverlaySlotSwitchSetting(group);
        AddPresetPreviewSetting(group);
        AddSlotCaptureSettings(group);
    }

    (ToggleSwitch Toggle, TextBlock Summary) IShortcutPageSettings.AddCharacterFollow(StackPanel body)
    {
        _characterFollowSummary = new TextBlock();
        BuildCharacterFollowControls(body);
        UpdateCharacterFollowSummary();
        return (_characterFollowToggle!, _characterFollowSummary);
    }
}
