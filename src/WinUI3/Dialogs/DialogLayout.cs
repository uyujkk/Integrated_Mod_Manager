using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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
}
