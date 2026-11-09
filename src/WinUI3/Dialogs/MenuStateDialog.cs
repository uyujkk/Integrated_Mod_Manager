using IntegratedModManager.Core.Presets;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>Menu-state variables found while saving a slot: which to keep anyway (moved from the window in stage J8).</summary>
internal static class MenuStateDialog
{
    /// <summary>The menu-state values to keep anyway, or null when cancelled.</summary>
    public static async Task<IReadOnlySet<ModPersistentValue>?> ChooseAsync(AppServices services, IReadOnlyList<ModPersistentValue> transient, int keptCount, double listMaxHeight)
    {
        string T(string key, params object?[] values) => services.Language.T(key, values);
        var checks = transient.Select(value => new CheckBox { Content = $"{value.Variable} = {value.Value}  ·  {value.RelativeIniPath}" }).ToArray();
        var list = new StackPanel { Spacing = 2 };
        foreach (CheckBox check in checks) list.Children.Add(check);
        var content = new StackPanel { Spacing = 10, MaxWidth = 560 };
        content.Children.Add(new TextBlock { Text = T("SlotCapture.MenuStateFound", transient.Count, keptCount), TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new ScrollViewer { Content = list, MaxHeight = listMaxHeight });
        var dialog = new ContentDialog
        {
            Title = T("SlotCapture.MenuStateTitle"), Content = content,
            PrimaryButtonText = T("SlotCapture.Continue"), CloseButtonText = T("Common.Cancel"), DefaultButton = ContentDialogButton.Primary
        };
        if (await services.Dialogs.ShowAsync(dialog) != ContentDialogResult.Primary) return null;
        return new HashSet<ModPersistentValue>(transient.Where((_, index) => checks[index].IsChecked == true));
    }
}
