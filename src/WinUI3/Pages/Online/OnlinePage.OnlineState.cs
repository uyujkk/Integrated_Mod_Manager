using IntegratedModManager.Core.Online;

namespace ModFolderCopier.WinUI;

public sealed partial class OnlinePage
{
    private readonly OnlinePageStateAdapter _onlineDetailState = new();

    private string GetOnlineDetailContextKey()
    {
        WorkspaceRepository? repository = _services.Repositories.Selected;
        return System.Text.Json.JsonSerializer.Serialize(new[]
        {
            repository?.Id, repository?.SourcePath, repository?.OnlineCategoryId,
            repository?.OnlineSourceSite, _services.Language.Current
        });
    }

    private bool IsOnlineDownloadActionBlocked(OnlineModCard mod)
        => _services.OnlineDownloads.DownloadActions.IsPreparing || _services.OnlineDownloads.DownloadActions.IsActive(
            OnlineDownloadService.CaptureOnlineDownloadContext(_services.Repositories.Selected), OnlineDownloadService.GetOnlineDownloadIdentity(mod));

    private void RefreshOnlineDetailActionState()
    {
        if (_services.IsClosing) return;
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

    private async Task RunOnlineCardDownloadAsync(OnlineModCard mod, Microsoft.UI.Xaml.Controls.Button button)
    {
        button.IsEnabled = false;
        try { await _services.OnlineDownloads.DownloadAndExtractOnlineModAsync(mod); }
        finally
        {
            if (!_services.IsClosing)
                button.IsEnabled = !string.IsNullOrWhiteSpace(mod.DownloadUrl) && !IsOnlineDownloadActionBlocked(mod);
        }
    }

}
