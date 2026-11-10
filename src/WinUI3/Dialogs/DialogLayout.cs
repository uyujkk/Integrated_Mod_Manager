using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Markup;

namespace ModFolderCopier.WinUI;

/// <summary>Pieces several dialogs share.</summary>
internal static class DialogLayout
{
    /// <summary>A label above its editor.</summary>
    public static UIElement LabeledEditor(string label, Control editor)
    {
        StackPanel panel = new() { Spacing = 6 };
        panel.Children.Add(new TextBlock
        {
            Text = label
        });
        panel.Children.Add(editor);
        return panel;
    }

    /// <summary>
    /// A group of settings in a dialog, drawn as a card like a SettingsCard group in Windows Settings: a title,
    /// an optional description and the controls.
    /// </summary>
    public static Border Section(string title, UIElement content, string? description = null)
    {
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = title, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], TextWrapping = TextWrapping.Wrap });
        if (description is not null) panel.Children.Add(Hint(description));
        panel.Children.Add(content);
        return new Border { Style = (Style)Application.Current.Resources["CardBorderStyle"], Child = panel };
    }

    /// <summary>Secondary text under a control: caption size in the secondary text colour.</summary>
    public static TextBlock Hint(string text = "") => new()
    {
        Text = text, TextWrapping = TextWrapping.Wrap,
        Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
    };

    /// <summary>The error of one field, shown right under it in the critical colour; hidden while empty.</summary>
    public static TextBlock FieldError()
    {
        var error = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"]
        };
        error.RegisterPropertyChangedCallback(TextBlock.TextProperty,
            (_, _) => error.Visibility = string.IsNullOrEmpty(error.Text) ? Visibility.Collapsed : Visibility.Visible);
        return error;
    }

    /// <summary>"Step 2 of 3" above a dialog's content, for flows that go through several dialogs.</summary>
    public static TextBlock Step(string text) => new()
    {
        Text = text, Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
    };

    /// <summary>
    /// A preview list grouped by what happens to each item (added, removed, kept, skipped), each group headed with
    /// its count, like the change lists of Windows Settings. Empty groups are left out; the list virtualizes.
    /// </summary>
    public static ListView GroupedPreview(IEnumerable<PreviewGroup> groups, DataTemplate itemTemplate)
    {
        var source = new CollectionViewSource { IsSourceGrouped = true, Source = groups.Where(group => group.Count > 0).ToList() };
        var list = new ListView
        {
            ItemsSource = source.View, SelectionMode = ListViewSelectionMode.None,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, ItemTemplate = itemTemplate
        };
        list.GroupStyle.Add(new GroupStyle
        {
            HeaderTemplate = (DataTemplate)XamlReader.Load("""
                <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                  <TextBlock Text="{Binding Header}" Style="{ThemeResource BodyStrongTextBlockStyle}"/>
                </DataTemplate>
                """)
        });
        return list;
    }

    /// <summary>Two lines per item: the item and what happens to it, with an optional detail line.</summary>
    public static DataTemplate PreviewRowTemplate() => (DataTemplate)XamlReader.Load("""
        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
          <StackPanel Padding="0,6" Spacing="2" ToolTipService.ToolTip="{Binding Detail}">
            <TextBlock Text="{Binding Title}" TextWrapping="Wrap"/>
            <TextBlock Text="{Binding ValueText}" Style="{ThemeResource CaptionTextBlockStyle}" Foreground="{ThemeResource TextFillColorSecondaryBrush}" TextWrapping="Wrap"/>
          </StackPanel>
        </DataTemplate>
        """);
}

/// <summary>One group of a grouped preview: its header (with the count) and its rows.</summary>
public sealed class PreviewGroup(string header, IEnumerable<CombinationPreviewRow> rows) : List<CombinationPreviewRow>(rows)
{
    public string Header { get; } = header;
}
