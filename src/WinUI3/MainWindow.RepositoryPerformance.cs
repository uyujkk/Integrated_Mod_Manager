using IntegratedModManager.Core.Repository;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{

    private RepositoryPerformanceRecorder.Scope? MeasureRepositoryUi(RepositoryPerformanceOperation operation, int count)
        => _services.Workspace.Performance.Measure(operation, count, width: RootGrid.ActualWidth,
            height: RootGrid.ActualHeight, scale: RootGrid.XamlRoot?.RasterizationScale ?? 0);
}
