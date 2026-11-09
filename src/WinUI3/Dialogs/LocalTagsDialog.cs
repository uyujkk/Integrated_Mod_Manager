using IntegratedModManager.Core.Repository;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>Editing the local tags of one or more Mods (moved from the window in stage J8).</summary>
internal static class LocalTagsDialog
{
    /// <summary>One Mod in the list: its name with its tags, whether it starts checked, and its tags now.</summary>
    public sealed record Entry(object Mod, string Label, bool Checked, IReadOnlyList<string> Tags);

    /// <summary>The edit to apply, or null when cancelled.</summary>
    public static async Task<(IReadOnlyList<object> Mods, string[] Tags, TagEditMode Mode)?> ShowAsync(AppServices services, IReadOnlyList<Entry> items, double windowHeight)
    {
        string T(string key) => services.Language.T(key);
        var body = new StackPanel { Spacing = 10, MinWidth = 300, MaxWidth = 580 };
        body.Children.Add(new TextBlock { Text = T("LocalTags.SelectModsToEditTagsAre"), TextWrapping = TextWrapping.Wrap });
        var mode = new ComboBox { Header = T("LocalTags.Action"), HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (string label in new[] { T("LocalTags.AddTags"), T("LocalTags.RemoveTags"), T("LocalTags.ReplaceAllTagsLeaveBlankTo") }) mode.Items.Add(label);
        mode.SelectedIndex = 0;
        body.Children.Add(mode);
        var input = new TextBox { Header = T("LocalTags.TagsCommaSeparatedUpTo16"), MaxLength = 1024,
            PlaceholderText = T("LocalTags.ToTryFavoriteNeedsRepair") };
        body.Children.Add(input);
        var checks = new List<(Entry Mod, CheckBox Check)>();
        var selectAll = new CheckBox { Content = T("LocalTags.SelectAllListedMods") };
        body.Children.Add(selectAll);
        foreach (var mod in items)
        {
            var check = new CheckBox { Content = mod.Label, IsChecked = mod.Checked };
            checks.Add((mod, check)); body.Children.Add(check);
        }
        selectAll.Click += (_, _) => { foreach (var item in checks) item.Check.IsChecked = selectAll.IsChecked == true; };
        var dialog = new ContentDialog { Title = T("LocalTags.EditLocalTags"), Content = new ScrollViewer { Content = body, MaxHeight = Math.Max(180, windowHeight - 280) },
            PrimaryButtonText = T("Common.Save"), CloseButtonText = T("Common.Cancel") };
        var validation = new TextBlock { TextWrapping = TextWrapping.Wrap };
        body.Children.Insert(3, validation);
        dialog.PrimaryButtonClick += (_, args) =>
        {
            string[] requested = input.Text.Split([',', '，', ';', '；', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (requested.Any(tag => tag.Length > 32 || tag.Any(char.IsControl)) || requested.Distinct(StringComparer.OrdinalIgnoreCase).Count() > 16)
            { validation.Text = T("LocalTags.UseUpTo16TagsEach"); args.Cancel = true; return; }
            if (!checks.Any(item => item.Check.IsChecked == true))
            { validation.Text = T("LocalTags.SelectAtLeastOneMod"); args.Cancel = true; }
            else if (mode.SelectedIndex == 0 && checks.Where(item => item.Check.IsChecked == true).Any(item =>
                item.Mod.Tags.Concat(requested).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 16))
            { validation.Text = T("LocalTags.SomeModsWouldExceed16Tags"); args.Cancel = true; }
        };
        if (await services.Dialogs.ShowAsync(dialog) != ContentDialogResult.Primary) return null;
        var tags = input.Text.Split([',', '，', ';', '；', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return (checks.Where(item => item.Check.IsChecked == true).Select(item => item.Mod.Mod).ToArray(), tags, (TagEditMode)mode.SelectedIndex);
    }
}
