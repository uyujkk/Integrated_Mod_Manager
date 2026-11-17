using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.DeveloperTools;
using IntegratedModManager.DeveloperTools.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace IntegratedModManager.DeveloperTools.Pages;

/// <summary>
/// A sanitized diagnostic ZIP for an issue report (written to the tools' own folder; never uploaded), and a copy of the
/// app's settings files. The preview shows the summary exactly as it goes into the ZIP.
/// </summary>
public sealed partial class DiagnosticsPage : UserControl, IToolPage
{
    private static readonly string[] TextFiles = ["beta-shell.json", "config.ini", "startup.log", "app-errors.log", "update-agent.log", "update.log"];
    private readonly ToolContext _context;

    internal DiagnosticsPage(ToolContext context)
    {
        _context = context;
        InitializeComponent();
        ApplyLanguage();
    }

    private string T(string key, params object?[] values) => _context.T(key, values);

    public void ApplyLanguage()
    {
        TitleText.Text = T("Nav.Diagnostics");
        IntroText.Text = T("Diagnostics.Intro");
        ExportCard.Header = T("Diagnostics.Export");
        ExportCard.Description = T("Diagnostics.ExportHint");
        ExportButton.Content = T("Diagnostics.ExportButton");
        SnapshotCard.Header = T("Diagnostics.Snapshot");
        SnapshotCard.Description = T("Diagnostics.SnapshotHint");
        SnapshotButton.Content = T("Diagnostics.SnapshotButton");
        FoldersCard.Header = T("Diagnostics.Folders");
        FoldersCard.Description = _context.SettingsDirectory;
        OpenDiagnosticsButton.Content = T("Diagnostics.OpenReports");
        OpenSnapshotsButton.Content = T("Diagnostics.OpenSnapshots");
        PreviewTitle.Text = T("Diagnostics.Preview");
    }

    public async Task RefreshAsync() => PreviewTextBox.Text = _context.Sanitize(await Task.Run(BuildSummary));

    private string BuildSummary()
    {
        var text = new StringBuilder();
        text.AppendLine("Integrated Mod Manager Developer Tools " + typeof(DiagnosticsPage).Assembly.GetName().Version);
        text.AppendLine($"Generated: {DateTimeOffset.Now:O}");
        text.AppendLine($"Root: {_context.RootPath}");
        text.AppendLine($"OS: {RuntimeInformation.OSDescription}");
        text.AppendLine($"Runtime: {RuntimeInformation.FrameworkDescription}");
        text.AppendLine($"App running: {ToolContext.IsMainAppRunning()}");
        text.AppendLine();
        text.Append(DiagnosticSummary.Build(_context.Shell, CacheInventory.Read(_context.RootPath)));
        text.AppendLine();

        // The record checks as counts per kind (no paths: the files below are sanitized, these lines need not be).
        var repositories = _context.Array("Repositories")
            .Select(node => new AuditRepository((string?)node?["Id"] ?? "", "", (string?)node?["SourcePath"] ?? "", (string?)node?["TargetPath"] ?? ""))
            .ToList();
        var records = _context.Array("CopyDeployments")
            .Select(node => node?.Deserialize<CopyDeploymentRecord>(ToolContext.JsonOptions)).OfType<CopyDeploymentRecord>().ToList();
        var slots = _context.Array("ModPersistentSlots")
            .Select(node => new AuditSlot((string?)node?["RepositoryId"] ?? "", (string?)node?["ModRelativePath"] ?? "", "", (string?)node?["PreviewFile"]))
            .ToList();
        AuditResult audit = StateRecordAudit.Run(repositories, records, slots, Path.Combine(_context.RootPath, "snapshots", "preset-previews"));
        text.AppendLine("[Record checks]");
        text.AppendLine($"Directory links in target folders: {audit.Links}");
        foreach (var group in audit.Findings.GroupBy(finding => finding.Kind).OrderBy(group => group.Key))
            text.AppendLine($"{group.Key}: {group.Count()}");
        if (audit.Findings.Count == 0) text.AppendLine("No findings");
        return text.ToString();
    }

    private async void OnExportClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_context.DiagnosticsDirectory);
            string output = Path.Combine(_context.DiagnosticsDirectory, $"IMM-Developer-Diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
            string summary = await Task.Run(BuildSummary);
            string root = _context.RootPath;
            await Task.Run(() =>
            {
                using ZipArchive archive = ZipFile.Open(output, ZipArchiveMode.Create);
                Write(archive, "summary.txt", _context.Sanitize(summary));
                foreach (string name in TextFiles)
                {
                    string path = Path.Combine(root, name);
                    if (!File.Exists(path)) continue;
                    try
                    {
                        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        using var reader = new StreamReader(stream);
                        Write(archive, "files/" + name, _context.Sanitize(reader.ReadToEnd()));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // An unreadable file does not spoil the rest of the report.
                    }
                }
            });
            PreviewTextBox.Text = _context.Sanitize(summary);
            _context.SetStatus(T("Diagnostics.Exported", output), InfoBarSeverity.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _context.SetStatus(T("Diagnostics.ExportFailed", ex.Message), InfoBarSeverity.Error);
        }
    }

    private static void Write(ZipArchive archive, string name, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private void OnSnapshotClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            string folder = Path.Combine(_context.SnapshotsDirectory, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(folder);
            int copied = 0;
            foreach (string path in new[] { _context.ShellConfigPath, _context.LegacyConfigPath })
            {
                if (!File.Exists(path)) continue;
                File.Copy(path, Path.Combine(folder, Path.GetFileName(path)), overwrite: false);
                copied++;
            }
            _context.SetStatus(T("Diagnostics.SnapshotDone", copied, folder), InfoBarSeverity.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _context.SetStatus(T("Diagnostics.SnapshotFailed", ex.Message), InfoBarSeverity.Error);
        }
    }

    private void OnOpenDiagnosticsClicked(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_context.DiagnosticsDirectory);
        _context.OpenFolder(_context.DiagnosticsDirectory);
    }

    private void OnOpenSnapshotsClicked(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_context.SnapshotsDirectory);
        _context.OpenFolder(_context.SnapshotsDirectory);
    }
}
