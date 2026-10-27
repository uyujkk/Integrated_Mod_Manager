namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private void TraceOnlineUiState(string stage, int itemId = 0)
    {
        if (_onlineWindowClosed) return;
        try
        {
            if (stage is "Details requested" or "Pane opening" or "Details publishing" or "Hero assigning decoded bitmap")
                UiCrashDiagnostics.RecordAction(stage, itemId, RootGrid?.ActualWidth ?? 0, RootGrid?.ActualHeight ?? 0);
            if (!UiCrashDiagnostics.Enabled) return;
            UiCrashDiagnostics.Write(stage,
            $"item={itemId} root={RootGrid?.ActualWidth:F2}x{RootGrid?.ActualHeight:F2} " +
            $"host={OnlineDetailsHost?.ActualWidth:F2} drawer={OnlineDetailDrawer?.Visibility} " +
            $"drawerWidth={OnlineDetailDrawer?.Width:F2} " +
            $"workspace={OnlineWorkspaceGrid?.ActualWidth:F2}x{OnlineWorkspaceGrid?.ActualHeight:F2} " +
            $"results={OnlineResultsHost?.ActualWidth:F2}x{OnlineResultsHost?.ActualHeight:F2} " +
            $"cards={_onlinePage.CardLayoutMode} closed={_onlineWindowClosed}");
        }
        catch { /* A disconnected XAML object must not turn tracing into a failure. */ }
    }
}
