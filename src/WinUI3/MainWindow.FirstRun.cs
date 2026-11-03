using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

// The first start after installing: the app supports several games, so it asks instead of assuming one.
public sealed partial class MainWindow
{
    private bool _firstInstall;

    private async Task ChooseFirstInstallGameAsync()
    {
        if (!_firstInstall) return;
        _firstInstall = false;
        if (GetSelectedRepository() is not { } repository) return;

        var games = new RadioButtons();
        foreach (RepositoryPresetInfo preset in BuiltInRepositoryPresets)
            games.Items.Add(new RadioButton { Content = L(preset.DisplayNameZh, preset.DisplayNameEn), Tag = preset.Id });
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock { Text = T("FirstRun.Explanation"), TextWrapping = TextWrapping.Wrap });
        content.Children.Add(games);
        var dialog = new ContentDialog
        {
            Title = T("FirstRun.Title"), Content = content, XamlRoot = RootGrid.XamlRoot, RequestedTheme = RootGrid.ActualTheme,
            PrimaryButtonText = T("FirstRun.Use"), CloseButtonText = T("FirstRun.Later"),
            DefaultButton = ContentDialogButton.Primary, IsPrimaryButtonEnabled = false
        };
        games.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = games.SelectedItem is not null;
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || _onlineWindowClosed) return;

        RepositoryPresetInfo? chosen = BuiltInRepositoryPresets.FirstOrDefault(preset => preset.Id == (games.SelectedItem as RadioButton)?.Tag as string);
        if (chosen is null) return;
        repository.Name = L(chosen.DisplayNameZh, chosen.DisplayNameEn);
        repository.OnlineSourceSite = DefaultOnlineSourceSite;
        repository.OnlineGameName = chosen.DisplayNameEn;
        repository.OnlineCategoryId = chosen.CategoryId;
        repository.WikiUrl = chosen.WikiUrl;
        ResetOnlineRepositoryContext();
        ApplySelectedRepositoryToInputs();
        SaveConfig();
        SaveShellConfig();
        RefreshDashboard();
        RefreshRepositoryActionButtons();
    }
}
