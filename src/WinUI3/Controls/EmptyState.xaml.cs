using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>
/// What an empty list shows: an icon, a title, an optional hint and optional actions.
/// An empty hint or missing actions take no space.
/// </summary>
public sealed partial class EmptyState : UserControl
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(EmptyState),
        new PropertyMetadata(string.Empty, (d, _) => ((EmptyState)d).Apply()));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(EmptyState),
        new PropertyMetadata(string.Empty, (d, _) => ((EmptyState)d).Apply()));

    public static readonly DependencyProperty HintProperty = DependencyProperty.Register(
        nameof(Hint), typeof(string), typeof(EmptyState),
        new PropertyMetadata(string.Empty, (d, _) => ((EmptyState)d).Apply()));

    public static readonly DependencyProperty ActionsProperty = DependencyProperty.Register(
        nameof(Actions), typeof(object), typeof(EmptyState),
        new PropertyMetadata(null, (d, _) => ((EmptyState)d).Apply()));

    private bool _showActions = true;

    public EmptyState()
    {
        InitializeComponent();
        Apply();
    }

    public string Glyph { get => (string)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    public string Hint { get => (string)GetValue(HintProperty); set => SetValue(HintProperty, value); }

    public object? Actions { get => GetValue(ActionsProperty); set => SetValue(ActionsProperty, value); }

    /// <summary>Hides the actions without removing them, for a message that offers no next step.</summary>
    public bool ShowActions
    {
        get => _showActions;
        set { _showActions = value; Apply(); }
    }

    private void Apply()
    {
        if (IconElement is null) return;
        IconElement.Glyph = Glyph ?? string.Empty;
        IconElement.Visibility = string.IsNullOrEmpty(Glyph) ? Visibility.Collapsed : Visibility.Visible;
        TitleElement.Text = Title ?? string.Empty;
        HintElement.Text = Hint ?? string.Empty;
        HintElement.Visibility = string.IsNullOrEmpty(Hint) ? Visibility.Collapsed : Visibility.Visible;
        ActionsElement.Content = Actions;
        ActionsElement.Visibility = Actions is not null && _showActions ? Visibility.Visible : Visibility.Collapsed;
    }
}
