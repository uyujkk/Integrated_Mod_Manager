using System.Numerics;
using IntegratedModManager.Core.Layout;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ModFolderCopier.WinUI;

// Keeps overlay text readable when the window background is very transparent: each control and label
// has its own backing, and free-standing text gets a shadow while the background is below 60%.
public sealed partial class MainWindow
{
    private int _overlayElementOpacity = OverlayAppearancePolicy.DefaultElementOpacity;
    private bool _overlayCompactView;
    private bool _overlayClickThrough;
    private ToggleButton? _overlayCompactToggle;
    private Slider? _overlayElementSlider;
    private Button? _overlayClickThroughButton;
    private FrameworkElement[] _overlayDetailRows = [];
    private readonly SolidColorBrush _overlayElementBrush = new();
    private readonly SolidColorBrush _overlayElementHoverBrush = new();
    private readonly List<(SpriteVisual Visual, DropShadow Shadow)> _overlayTextShadows = [];

    private bool OverlayUsesLightBacking =>
        OverlayAppearancePolicy.UsesLightBacking(_overlayTextBrush.Color.R, _overlayTextBrush.Color.G, _overlayTextBrush.Color.B);

    // The brushes are shared, so changing them restyles everything in the open overlay.
    private void ApplyOverlayAppearance()
    {
        bool light = OverlayUsesLightBacking;
        byte fill = light ? (byte)235 : (byte)45;
        byte hover = light ? (byte)215 : (byte)68;
        _overlayElementBrush.Color = Color.FromArgb(OverlayAppearancePolicy.ElementAlpha(_overlayElementOpacity), fill, fill, (byte)(fill + (light ? 0 : 10)));
        _overlayElementHoverBrush.Color = Color.FromArgb(OverlayAppearancePolicy.HoverAlpha(_overlayElementOpacity), hover, hover, (byte)(hover + (light ? 0 : 10)));
        bool show = OverlayAppearancePolicy.ShowTextShadow(_shortcuts.OverlayOpacity);
        Color shadowColor = light ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(255, 0, 0, 0);
        foreach ((SpriteVisual visual, DropShadow shadow) in _overlayTextShadows)
        {
            visual.IsVisible = show;
            shadow.Color = shadowColor;
        }
    }

    // Lightweight styling: the controls' own templates pick these up in every visual state.
    private void ApplyOverlayElementResources(FrameworkElement root)
    {
        foreach (string control in new[] { "Button", "ToggleButton", "ComboBox" })
        {
            root.Resources[control + "Background"] = _overlayElementBrush;
            root.Resources[control + "BackgroundDisabled"] = _overlayElementBrush;
            root.Resources[control + "BackgroundPointerOver"] = _overlayElementHoverBrush;
            root.Resources[control + "BackgroundPressed"] = _overlayElementHoverBrush;
        }
        root.Resources["ComboBoxPlaceHolderForeground"] = _overlayTextBrush;
        root.Resources["ComboBoxBackgroundFocused"] = _overlayElementHoverBrush;
        root.Resources["ComboBoxBackgroundUnfocused"] = _overlayElementBrush;
    }

    private Border OverlayLabel(TextBlock text) => new()
    {
        Child = WithOverlayTextShadow(text), Background = _overlayElementBrush, CornerRadius = new CornerRadius(4),
        Padding = new Thickness(6, 2, 6, 2), VerticalAlignment = text.VerticalAlignment
    };

    private FrameworkElement WithOverlayTextShadow(TextBlock text)
    {
        var anchor = new Canvas { IsHitTestVisible = false };
        var host = new Grid { VerticalAlignment = text.VerticalAlignment };
        host.Children.Add(anchor);
        host.Children.Add(text);
        Compositor compositor = ElementCompositionPreview.GetElementVisual(anchor).Compositor;
        DropShadow shadow = compositor.CreateDropShadow();
        shadow.BlurRadius = 3;
        shadow.Offset = new Vector3(1, 1, 0);
        shadow.Opacity = .95f;
        SpriteVisual visual = compositor.CreateSpriteVisual();
        visual.Shadow = shadow;
        ElementCompositionPreview.SetElementChildVisual(anchor, visual);
        // The mask is a snapshot of the text, so it must follow text and size changes.
        void Refresh()
        {
            if (text.ActualWidth <= 0 || text.ActualHeight <= 0) return;
            visual.Size = new Vector2((float)text.ActualWidth, (float)text.ActualHeight);
            shadow.Mask = text.GetAlphaMask();
        }
        text.SizeChanged += (_, _) => Refresh();
        text.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) => Refresh());
        _overlayTextShadows.Add((visual, shadow));
        visual.IsVisible = OverlayAppearancePolicy.ShowTextShadow(_shortcuts.OverlayOpacity);
        shadow.Color = OverlayUsesLightBacking ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(255, 0, 0, 0);
        return host;
    }

    // Two ways to keep the overlay out of the way in game: fewer rows, and ignoring the mouse.
    private Grid CreateOverlayViewRow()
    {
        _overlayCompactToggle = new ToggleButton
        {
            Content = T("OverlayView.Compact"), IsChecked = _overlayCompactView, Foreground = _overlayTextBrush,
            HorizontalAlignment = HorizontalAlignment.Stretch, AllowFocusOnInteraction = false, FontSize = 13
        };
        ToolTipService.SetToolTip(_overlayCompactToggle, T("OverlayView.CompactHint"));
        AutomationProperties.SetAutomationId(_overlayCompactToggle, "OverlayCompactButton");
        _overlayCompactToggle.Click += (_, _) =>
        {
            _overlayCompactView = _overlayCompactToggle.IsChecked == true;
            ApplyOverlayCompactView();
            SaveShellConfig();
        };
        _overlayClickThroughButton = new Button
        {
            Content = T("OverlayView.ClickThrough"), Foreground = _overlayTextBrush,
            HorizontalAlignment = HorizontalAlignment.Stretch, AllowFocusOnInteraction = false, FontSize = 13
        };
        ToolTipService.SetToolTip(_overlayClickThroughButton, T("OverlayView.ClickThroughHint"));
        AutomationProperties.SetAutomationId(_overlayClickThroughButton, "OverlayClickThroughButton");
        _overlayClickThroughButton.Click += (_, _) => SetOverlayClickThrough(true);
        var row = new Grid { ColumnSpacing = 6 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(_overlayClickThroughButton, 1);
        row.Children.Add(_overlayCompactToggle);
        row.Children.Add(_overlayClickThroughButton);
        return row;
    }

    private void ApplyOverlayCompactView()
    {
        Visibility visibility = _overlayCompactView ? Visibility.Collapsed : Visibility.Visible;
        foreach (FrameworkElement row in _overlayDetailRows) row.Visibility = visibility;
    }

    private void SetOverlayClickThrough(bool enabled)
    {
        if (_shortcutOverlay is null) return;
        // Without the hotkey the user could not get the mouse back, short of closing the app.
        if (enabled && (!_shortcuts.HotkeyEnabled || _shortcutPanelHotkey is null))
        {
            _shortcutStatus!.Text = T("OverlayView.ClickThroughNeedsHotkey");
            if (_overlayCompactView) { _overlayCompactView = false; ApplyOverlayCompactView(); _overlayCompactToggle!.IsChecked = false; }
            RefreshShortcutOverlay(bindingsChanged: true);
            return;
        }
        _overlayClickThrough = enabled;
        _shortcutOverlay.SetClickThrough(enabled);
        // The status row is hidden in the compact view, and this is the one thing that must stay visible.
        _overlayClickThroughButton!.Content = enabled ? T("OverlayView.ClickThroughActive") : T("OverlayView.ClickThrough");
        _shortcutStatus!.Text = enabled ? T("OverlayView.ClickThroughOn") : T("OverlayView.ClickThroughOff");
        RefreshShortcutOverlay(bindingsChanged: true);
    }

    private StackPanel CreateOverlayAppearancePresetRow()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (OverlayAppearancePreset preset in OverlayAppearancePreset.All)
        {
            string label = preset.Id switch
            {
                "clear" => T("OverlayView.Preset.clear"),
                "balanced" => T("OverlayView.Preset.balanced"),
                _ => T("OverlayView.Preset.minimal")
            };
            var button = new Button { Content = label };
            button.Click += (_, _) => ApplyOverlayAppearancePreset(preset);
            row.Children.Add(button);
        }
        return row;
    }

    private void ApplyOverlayAppearancePreset(OverlayAppearancePreset preset)
    {
        _shortcuts.OverlayOpacity = preset.BackgroundOpacity;
        _overlayElementOpacity = preset.ElementOpacity;
        _overlayCompactView = preset.CompactView;
        _shortcutOverlay?.SetBackgroundOpacity(_shortcuts.OverlayOpacity);
        ApplyOverlayAppearance();
        ApplyOverlayCompactView();
        if (_overlayCompactToggle is not null) _overlayCompactToggle.IsChecked = _overlayCompactView;
        // The sliders ignore a value that already matches the field, so moving them changes nothing twice.
        if (_shortcutOpacitySlider is not null) _shortcutOpacitySlider.Value = _shortcuts.OverlayOpacity;
        if (_overlayOpacitySlider is not null) _overlayOpacitySlider.Value = _shortcuts.OverlayOpacity;
        if (_overlayElementSlider is not null) _overlayElementSlider.Value = _overlayElementOpacity;
        SaveShellConfig();
    }

    private Slider CreateOverlayElementOpacitySlider()
    {
        var slider = new Slider
        {
            Header = T("ShortcutPanel.OverlayElementOpacity"), Minimum = 0, Maximum = 100, StepFrequency = 5,
            SmallChange = 5, Value = _overlayElementOpacity
        };
        // Same guard as the background slider: ignore values raised while the template loads.
        bool live = false;
        slider.Loaded += (_, _) =>
        {
            if (live) return;
            slider.Value = _overlayElementOpacity;
            live = true;
        };
        slider.ValueChanged += (_, e) =>
        {
            if (!live || (int)Math.Round(e.NewValue) == _overlayElementOpacity) return;
            _overlayElementOpacity = (int)Math.Round(e.NewValue);
            ApplyOverlayAppearance();
            SaveShellConfig();
        };
        return slider;
    }
}
