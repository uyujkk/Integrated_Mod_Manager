using IntegratedModManager.Core.Configuration;

namespace ModFolderCopier.WinUI;

// The first start after installing: the app supports several games, so it asks instead of assuming one,
// then the getting-started tips point at the dashboard's main places (stage J4-b).
public sealed partial class MainWindow
{
    private bool _firstInstall;
    private GettingStartedTips _gettingStartedTips = GettingStartedTips.Load(firstInstall: false, savedPending: false);

    private async Task ChooseFirstInstallGameAsync()
    {
        if (!_firstInstall) return;
        _firstInstall = false;
        if (GetSelectedRepository() is not { } repository) return;

        RepositoryPresetInfo? chosen = await FirstRunDialog.ShowAsync(_services, BuiltInRepositoryPresets, RootGrid.ActualTheme);
        if (chosen is null || _onlineView._onlineWindowClosed) return;
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

    /// <summary>
    /// Hands the pending tips to the dashboard once the startup dialogs (the game, what's new, an update
    /// prompt) are closed; the dashboard shows them while it is on screen.
    /// </summary>
    private async Task ShowGettingStartedTipsAsync()
    {
        if (!_gettingStartedTips.IsPending) return;
        await Task.Delay(1500);
        for (int attempt = 0; attempt < 300 && IsContentDialogOpen(); attempt++)
        {
            await Task.Delay(1000);
        }
        if (_onlineView._onlineWindowClosed || !_gettingStartedTips.IsPending) return;
        _services.Dashboard.GettingStarted = _gettingStartedTips;
    }

    private void OnGettingStartedTipsClosed()
    {
        _services.Dashboard.GettingStarted = null;
        SaveShellConfig();
    }
}
