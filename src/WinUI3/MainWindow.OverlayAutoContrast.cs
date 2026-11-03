using IntegratedModManager.Core.Layout;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace ModFolderCopier.WinUI;

// Optional: while the overlay is very transparent, look at what is behind it every couple of seconds and use
// dark or light text to match. The user's own colour comes back as soon as the condition ends.
public sealed partial class MainWindow
{
    private static readonly Color AutoDarkText = Color.FromArgb(255, 32, 32, 32), AutoLightText = Color.FromArgb(255, 245, 245, 245);
    private bool _overlayAutoContrast;
    private Color _overlayUserTextColor = Color.FromArgb(255, 255, 255, 255);
    private Color? _overlayAutoTextColor;
    private DispatcherQueueTimer? _overlayContrastTimer;

    private void AddOverlayAutoContrastSetting(StackPanel pane)
    {
        var toggle = new ToggleSwitch
        {
            OnContent = T("Common.On2"), OffContent = T("Common.Off2"), Header = T("OverlayView.AutoContrast"),
            IsOn = _overlayAutoContrast, IsEnabled = ShortcutOverlayWindow.CanExcludeFromCapture
        };
        ToolTipService.SetToolTip(toggle, ShortcutOverlayWindow.CanExcludeFromCapture ? T("OverlayView.AutoContrastHint") : T("OverlayView.AutoContrastUnsupported"));
        toggle.Toggled += (_, _) =>
        {
            _overlayAutoContrast = toggle.IsOn;
            UpdateOverlayContrastTimer();
            if (!_overlayAutoContrast) RestoreOverlayTextColor();
            SaveShellConfig();
        };
        pane.Children.Add(toggle);
    }

    private void UpdateOverlayContrastTimer()
    {
        bool wanted = _overlayAutoContrast && ShortcutOverlayWindow.CanExcludeFromCapture;
        if (wanted && _overlayContrastTimer is null)
        {
            _overlayContrastTimer = DispatcherQueue.CreateTimer();
            _overlayContrastTimer.Interval = TimeSpan.FromSeconds(1.5);
            _overlayContrastTimer.Tick += (_, _) => CheckOverlayContrast();
        }
        if (wanted) _overlayContrastTimer!.Start();
        else _overlayContrastTimer?.Stop();
    }

    private void CheckOverlayContrast()
    {
        // Only when the scene shows through and nothing else is going on in the overlay.
        if (_shortcutOverlay is not { } overlay || !_shortcutOverlayVisible || overlay.ShowsLayer || _savingOverlaySinglePreset
            || !OverlayAppearancePolicy.ShowTextShadow(_shortcutOverlayOpacity) || overlay.ClientArea is not { } area)
        {
            RestoreOverlayTextColor();
            return;
        }
        byte[]? pixels;
        overlay.BeginCaptureExclusion();
        try { pixels = CharacterScreenCapture.CaptureScreenArea(area); }
        finally { overlay.EndCaptureExclusion(); }
        if (pixels is null) return;
        bool darkNow = _overlayAutoTextColor == AutoDarkText;
        Color wanted = OverlayAppearancePolicy.PrefersDarkText(OverlayAppearancePolicy.AverageLuminance(pixels), darkNow) ? AutoDarkText : AutoLightText;
        if (_overlayAutoTextColor == wanted) return;
        if (_overlayAutoTextColor is null) _overlayUserTextColor = _overlayTextBrush.Color;
        _overlayAutoTextColor = wanted;
        _overlayTextBrush.Color = wanted;
        ApplyOverlayAppearance();
    }

    private void RestoreOverlayTextColor()
    {
        if (_overlayAutoTextColor is null) return;
        _overlayAutoTextColor = null;
        _overlayTextBrush.Color = _overlayUserTextColor;
        ApplyOverlayAppearance();
    }
}
