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
