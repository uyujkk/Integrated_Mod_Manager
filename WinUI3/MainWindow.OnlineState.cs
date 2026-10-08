using IntegratedModManager.Core;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private readonly OnlinePageStateAdapter _onlineDetailState = new();
    private readonly OnlineDownloadActionAdapter _onlineDownloadActions = new();
    private int _onlineInstallBusyCount;
    private bool _onlineWindowClosed;

    private string GetOnlineDetailContextKey()
    {
        WorkspaceRepository? repository = GetSelectedRepository();
        return System.Text.Json.JsonSerializer.Serialize(new[]
        {
            repository?.Id, repository?.SourcePath, repository?.OnlineCategoryId,
            repository?.OnlineSourceSite, _currentLanguage.ToString()
        });
    }

    private static string GetOnlineDownloadIdentity(OnlineModCard mod)
        => mod.ItemId > 0 ? $"GameBanana:{mod.ItemId}" : mod.ProfileUrl;

    private bool IsOnlineDownloadActionBlocked(OnlineModCard mod)
        => _onlineDownloadActions.IsPreparing || _onlineDownloadActions.IsActive(
            CaptureOnlineDownloadContext(GetSelectedRepository()), GetOnlineDownloadIdentity(mod));

    private void RefreshOnlineDetailActionState()
    {
        if (_onlineWindowClosed) return;
        if (_activeOnlineDetailMod is not OnlineModCard mod) return;
        UpdateOnlineDetailDownloadControls(mod);
        if (_onlineDetailState.IsLoading)
        {
            DownloadOnlineDetailButton.IsEnabled = false;
            SelectOnlineDetailDownloadButton.IsEnabled = false;
        }
    }

    private void InvalidateOnlineDetailRequest()
    {
        _onlineDetailState.Invalidate();
        _onlineHeroImageRequestVersion++;
    }

    private void SetOnlineInstallBusy(bool busy)
    {
        _onlineInstallBusyCount = Math.Max(0, _onlineInstallBusyCount + (busy ? 1 : -1));
        SetBusyState(_onlineInstallBusyCount > 0);
    }

    private async Task RunOnlineCardDownloadAsync(OnlineModCard mod, Microsoft.UI.Xaml.Controls.Button button)
    {
        button.IsEnabled = false;
        try { await DownloadAndExtractOnlineModAsync(mod); }
        finally
        {
            if (!_onlineWindowClosed)
                button.IsEnabled = !string.IsNullOrWhiteSpace(mod.DownloadUrl) && !IsOnlineDownloadActionBlocked(mod);
        }
    }

    private async Task ReportOnlineActionErrorAsync(string message, string title)
    {
        if (_onlineWindowClosed) return;
        try { await ShowMessageAsync(message, title); }
        catch (Exception ex) { LogApplicationIssue("Report online action error", ex); }
    }
}
