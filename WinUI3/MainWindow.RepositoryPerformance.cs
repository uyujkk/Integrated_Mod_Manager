using IntegratedModManager.Core;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private static readonly object RepositoryPerformanceLogSync = new();
    // Explicit opt-in marker, read once. No settings/schema changes, no upload.
    private readonly RepositoryPerformanceRecorder _repositoryPerformance = new(
        File.Exists(Path.Combine(AppContext.BaseDirectory, "repository-performance.enabled")), WriteRepositoryPerformance);

    private static void WriteRepositoryPerformance(RepositoryPerformanceSample sample)
    {
        string file = Path.Combine(AppContext.BaseDirectory, "repository-performance.log");
        lock (RepositoryPerformanceLogSync)
        {
            if (File.Exists(file) && new FileInfo(file).Length >= 1024 * 1024) return;
            File.AppendAllText(file, JsonSerializer.Serialize(sample,
                new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } }) + Environment.NewLine);
        }
    }

    private RepositoryPerformanceRecorder.Scope? MeasureRepositoryUi(RepositoryPerformanceOperation operation, int count)
        => _repositoryPerformance.Measure(operation, count, width: RootGrid.ActualWidth,
            height: RootGrid.ActualHeight, scale: RootGrid.XamlRoot?.RasterizationScale ?? 0);
}
