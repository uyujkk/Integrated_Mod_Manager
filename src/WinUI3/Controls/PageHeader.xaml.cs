using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>
/// The single-row page header: a title, an info icon whose tooltip holds the page description,
/// and an optional row of actions on the right.
/// </summary>
public sealed partial class PageHeader : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(PageHeader), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(PageHeader),
        new PropertyMetadata(string.Empty, (sender, _) => ((PageHeader)sender).OnDescriptionChanged()));

    public static readonly DependencyProperty ActionsProperty = DependencyProperty.Register(
        nameof(Actions), typeof(object), typeof(PageHeader), new PropertyMetadata(null));

    public PageHeader()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }

    public static Visibility VisibleWhenText(string? text)
        => string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;

    // Screen readers cannot open the tooltip, so the description is also the icon's name.
    private void OnDescriptionChanged() => AutomationProperties.SetName(InfoIcon, Description);
}
