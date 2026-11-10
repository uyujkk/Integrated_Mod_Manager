using IntegratedModManager.Core.Online;


namespace ModFolderCopier.WinUI;

public sealed partial class OnlinePage
{
    internal readonly OnlinePageViewModel _onlinePage = new();
    private readonly OnlinePageStateAdapter _onlineDetailState = new();
    internal readonly OnlineDownloadActionAdapter _onlineDownloadActions = new();
    private int _onlineInstallBusyCount;
    internal bool _onlineWindowClosed;

    private string GetOnlineDetailContextKey()
    {
        WorkspaceRepository? repository = _host.GetSelectedRepository();
        return System.Text.Json.JsonSerializer.Serialize(new[]
        {
            repository?.Id, repository?.SourcePath, repository?.OnlineCategoryId,
            repository?.OnlineSourceSite, _services.Language.Current
        });
    }

    internal static string GetOnlineDownloadIdentity(OnlineModCard mod)
        => mod.ItemId > 0 ? $"GameBanana:{mod.ItemId}" : mod.ProfileUrl;

    private bool IsOnlineDownloadActionBlocked(OnlineModCard mod)
        => _onlineDownloadActions.IsPreparing || _onlineDownloadActions.IsActive(
            CaptureOnlineDownloadContext(_host.GetSelectedRepository()), GetOnlineDownloadIdentity(mod));

    private void RefreshOnlineDetailActionState()
    {
        if (_onlineWindowClosed) return;
        RefreshOnlineCardStates();
        if (_activeOnlineDetailMod is not OnlineModCard mod) return;
        UpdateOnlineDetailDownloadControls(mod);
        if (_onlineDetailState.IsLoading)
        {
            DownloadOnlineDetailButton.IsEnabled = false;
            SelectOnlineDetailDownloadButton.IsEnabled = false;
        }
    }

    internal void InvalidateOnlineDetailRequest()
    {
        _onlineDetailState.Invalidate();
        _onlineHeroImageRequestVersion++;
    }

    private void SetOnlineInstallBusy(bool busy)
    {
        _onlineInstallBusyCount = Math.Max(0, _onlineInstallBusyCount + (busy ? 1 : -1));
        _host.SetBusyState(_onlineInstallBusyCount > 0);
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

    internal async Task ReportOnlineActionErrorAsync(string message, string title, Exception? error = null)
    {
        if (_onlineWindowClosed) return;
        try { await _services.Dialogs.ErrorAsync(title, message, error); }
        catch (Exception ex) { _host.LogApplicationIssue("Report online action error", ex); }
    }
}
