using System.Numerics;
using IntegratedModManager.Core.Layout;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ModFolderCopier.WinUI;

// Keeps overlay text readable when the window background is very transparent: each control and label
// has its own backing, and free-standing text gets a shadow while the background is below 60%.
public sealed partial class MainWindow
{
    private int _overlayElementOpacity = OverlayAppearancePolicy.DefaultElementOpacity;
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
        bool show = OverlayAppearancePolicy.ShowTextShadow(_shortcutOverlayOpacity);
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
        visual.IsVisible = OverlayAppearancePolicy.ShowTextShadow(_shortcutOverlayOpacity);
        shadow.Color = OverlayUsesLightBacking ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(255, 0, 0, 0);
        return host;
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
