using IntegratedModManager.Core.Shortcuts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>
/// A key combination drawn as separate key caps ("Ctrl" "Shift" "H"). Narrator reads the whole combination.
/// Stage J5; the Mod workspace's Shortcuts tab can use it too.
/// </summary>
public sealed partial class KeyVisual : UserControl
{
    public static readonly DependencyProperty KeysProperty = DependencyProperty.Register(
        nameof(Keys), typeof(string), typeof(KeyVisual),
        new PropertyMetadata(string.Empty, (sender, _) => ((KeyVisual)sender).OnKeysChanged()));

    public KeyVisual()
    {
        InitializeComponent();
    }

    /// <summary>The keys joined with "+", as a binding shows them.</summary>
    public string Keys
    {
        get => (string)GetValue(KeysProperty);
        set => SetValue(KeysProperty, value);
    }

    private void OnKeysChanged()
    {
        KeyItems.ItemsSource = ShortcutKeyCaps.Split(Keys);
        AutomationProperties.SetName(this, Keys);
    }
}
