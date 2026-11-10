using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>One action for several Mods: add to a preset, copy, delete or export (moved from the window in stage J8).</summary>
internal static class BatchModActionsDialog
{
    /// <summary>0 add to preset, 1 copy to a folder, 2 delete, 3 export a bundle.</summary>
    public sealed record Choice(int Action, IReadOnlyList<SecondLevelFolderItem> Mods, string? ProfileId);

    public static async Task<Choice?> ShowAsync(AppServices services, IReadOnlyList<SecondLevelFolderItem> items, SecondLevelFolderItem? selected,
        IReadOnlyList<ModConfigurationProfile> profiles, string? selectedProfileId, double windowHeight)
    {
        string T(string key) => services.Language.T(key);
        var action = new ComboBox { Header = T("LocalTags.Action"), HorizontalAlignment = HorizontalAlignment.Stretch };
        action.Items.Add(T("BatchMods.AddToPreset"));
        action.Items.Add(T("BatchMods.CopyToFolder"));
        action.Items.Add(T("BatchMods.Delete"));
        action.Items.Add(T("BatchMods.ExportBundle"));
        action.SelectedIndex = 0;
        var profile = new ComboBox { Header = T("BatchMods.Preset"), HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = T("ConfigurationProfiles.NoProfilesForThisRepository") };
        foreach (ModConfigurationProfile item in profiles) profile.Items.Add(new ComboBoxItem { Content = item.Name, Tag = item.Id });
        profile.SelectedItem = profile.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == selectedProfileId) ?? profile.Items.FirstOrDefault();
        action.SelectionChanged += (_, _) => profile.Visibility = action.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;

        var body = new StackPanel { Spacing = 10, MinWidth = 300, MaxWidth = 580 };
        body.Children.Add(action);
        body.Children.Add(profile);
        TextBlock validation = DialogLayout.FieldError();
        body.Children.Add(validation);
        var selectAll = new CheckBox { Content = T("LocalTags.SelectAllListedMods") };
        body.Children.Add(selectAll);
        var checks = items.Select(mod => (Mod: mod, Check: new CheckBox { Content = mod.Name, IsChecked = ReferenceEquals(mod, selected) })).ToArray();
        foreach (var item in checks) body.Children.Add(item.Check);
        selectAll.Click += (_, _) => { foreach (var item in checks) item.Check.IsChecked = selectAll.IsChecked == true; };
        var dialog = new ContentDialog
        {
            Title = T("BatchMods.Title"), Content = new ScrollViewer { Content = body, MaxHeight = Math.Max(180, windowHeight - 280) },
            PrimaryButtonText = T("BatchMods.Run"), CloseButtonText = T("Common.Cancel")
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (!checks.Any(item => item.Check.IsChecked == true)) { validation.Text = T("LocalTags.SelectAtLeastOneMod"); args.Cancel = true; }
            else if (action.SelectedIndex == 0 && profile.SelectedItem is null) { validation.Text = T("BatchMods.ChooseAPreset"); args.Cancel = true; }
        };
        if (await services.Dialogs.ShowAsync(dialog) != ContentDialogResult.Primary) return null;
        return new Choice(action.SelectedIndex, checks.Where(item => item.Check.IsChecked == true).Select(item => item.Mod).ToArray(),
            (profile.SelectedItem as ComboBoxItem)?.Tag as string);
    }
}
