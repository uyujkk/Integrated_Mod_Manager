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

        RepositoryPresetInfo? chosen = await FirstRunDialog.ShowAsync(_services, BuiltInRepositoryPresets, RootGrid.ActualTheme);
        if (chosen is null || _onlineWindowClosed) return;
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
