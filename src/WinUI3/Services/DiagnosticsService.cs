using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using IntegratedModManager.Core.Infrastructure;
using IntegratedModManager.Core.Updates;
using Windows.System;

namespace ModFolderCopier.WinUI;

/// <summary>
/// Diagnostic reports from Settings (stage J finish, step 8a: moved from the window unchanged): the app, system and
/// sanitized settings summary and the recent logs in a zip, for the user to look at and attach to an issue.
/// </summary>
internal sealed class DiagnosticsService(AppServices services)
{
    private const string AppVersion = ApplicationReleasePolicy.VersionLabel;
    private const string GitHubRepositoryUrl = "https://github.com/uyujkk/Integrated_Mod_Manager";

    private readonly AppServices _services = services;
    private Action<string, Exception> _reportFailure = (_, _) => { };
    private Func<string> _dataStoreHealth = () => string.Empty;
    private Func<string> _installRoot = () => AppContext.BaseDirectory;

    /// <summary>Called once by the window: reporting a failed action, the local database's health line, and the install folder.</summary>
    public void Attach(Action<string, Exception> reportFailure, Func<string> dataStoreHealth, Func<string> installRoot)
    {
        _reportFailure = reportFailure;
        _dataStoreHealth = dataStoreHealth;
        _installRoot = installRoot;
    }

    private string T(string key, params object?[] values) => _services.Language.T(key, values);

    /// <summary>Settings: writes a sanitized report into the diagnostics folder and opens the folder.</summary>
    public async void Export()
    {
        try
        {
            string path = await CreateDiagnosticReportAsync();
            _services.Settings.DiagnosticsStatus = T("ProductivityFeatures.Exported", Path.GetFileName(path));
            Process.Start(new ProcessStartInfo { FileName = Path.GetDirectoryName(path)!, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _reportFailure("OnExportDiagnosticsClicked", ex);
        }
    }

    /// <summary>Settings: writes the report and, when confirmed, opens a new GitHub issue to attach it to.</summary>
    public async void Submit()
    {
        try
        {
            string path = await CreateDiagnosticReportAsync();
            bool confirmed = await _services.Dialogs.ConfirmAsync(T("ProductivityFeatures.ReportAnIssue"),
                T("ProductivityFeatures.DiagnosticReportCreatedGitHubSNew", Path.GetFileName(path)), T("Dialogs.OpenIssuePage"));
            if (!confirmed)
            {
                return;
            }

            string body = Uri.EscapeDataString($"App version: {AppVersion}\nOS: {Environment.OSVersion.VersionString}\nArchitecture: {RuntimeInformation.OSArchitecture}\n\nA sanitized diagnostic report was generated locally: {Path.GetFileName(path)}\nPlease attach it after reviewing the archive.");
            string url = $"{GitHubRepositoryUrl}/issues/new?title={Uri.EscapeDataString("Bug report " + AppVersion)}&body={body}";
            await Launcher.LaunchUriAsync(new Uri(url));
            Process.Start(new ProcessStartInfo { FileName = Path.GetDirectoryName(path)!, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _reportFailure("OnSubmitDiagnosticsClicked", ex);
        }
    }

    private async Task<string> CreateDiagnosticReportAsync()
    {
        string diagnosticsRoot = Path.Combine(AppContext.BaseDirectory, "diagnostics");
        Directory.CreateDirectory(diagnosticsRoot);
        string reportPath = Path.Combine(diagnosticsRoot, $"IntegratedModManager-Diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        await using FileStream output = new(reportPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create);

        var summary = new StringBuilder();
        summary.AppendLine($"Integrated Mod Manager diagnostics");
        summary.AppendLine($"Generated (UTC): {DateTimeOffset.UtcNow:O}");
        summary.AppendLine($"App version: {AppVersion}");
        summary.AppendLine($"OS: {Environment.OSVersion.VersionString}");
        summary.AppendLine($"OS architecture: {RuntimeInformation.OSArchitecture}");
        summary.AppendLine($"Process architecture: {RuntimeInformation.ProcessArchitecture}");
        summary.AppendLine($".NET: {RuntimeInformation.FrameworkDescription}");
        summary.AppendLine($"Language: {_services.Language.Current}");
        summary.AppendLine($"Theme: {(_services.Appearance.IsDark ? "Dark" : "Light")}");
        summary.AppendLine($"Repository count: {_services.Repositories.Items.Count}");
        summary.AppendLine($"Source paths configured: {_services.Repositories.Items.Count(item => !string.IsNullOrWhiteSpace(item.SourcePath))}");
        summary.AppendLine($"Target paths configured: {_services.Repositories.Items.Count(item => !string.IsNullOrWhiteSpace(item.TargetPath))}");
        summary.AppendLine($"SQLite: {_dataStoreHealth()}");
        WriteZipText(archive, "report.txt", summary.ToString());

        var sanitizedConfig = new
        {
            AppVersion,
            Language = _services.Language.Current,
            Theme = _services.Appearance.IsDark ? "Dark" : "Light",
            _services.ShellSettings.CheckAppUpdatesOnStartup,
            UpdateInterval = _services.ShellSettings.UpdateCheckInterval.ToString(),
            _services.ShellSettings.InstallBackupLimitGb,
            ReduceMotion = _services.ShellSettings.ReduceMotion,
            Density = _services.ShellSettings.InterfaceDensity.ToString(),
            Repositories = _services.Repositories.Items.Select((repository, index) => new
            {
                Number = index + 1,
                HasSourcePath = !string.IsNullOrWhiteSpace(repository.SourcePath),
                HasTargetPath = !string.IsNullOrWhiteSpace(repository.TargetPath),
                HasLauncher = !string.IsNullOrWhiteSpace(repository.LauncherPath),
                OnlineSource = repository.OnlineSourceSite,
                OnlineGame = repository.OnlineGameName,
                OnlineCategoryId = repository.OnlineCategoryId,
                HasWiki = !string.IsNullOrWhiteSpace(repository.WikiUrl),
                CharacterMappingCount = repository.CharacterCategoryMappings?.Count ?? 0
            }).ToArray()
        };
        WriteZipText(archive, "config-summary.json", JsonSerializer.Serialize(sanitizedConfig, new JsonSerializerOptions { WriteIndented = true }));

        foreach (string logPath in EnumerateDiagnosticLogs())
        {
            try
            {
                string text = await File.ReadAllTextAsync(logPath);
                if (text.Length > 250_000)
                {
                    text = text[^250_000..];
                }
                WriteZipText(archive, "logs/" + Path.GetFileName(logPath), DiagnosticText.Sanitize(text));
            }
            catch (Exception ex)
            {
                AppLog.LogApplicationIssue("Add log to diagnostic report", ex);
            }
        }
        return reportPath;
    }

    private IEnumerable<string> EnumerateDiagnosticLogs()
    {
        foreach (string name in new[] { "ui-action-context.json", "ui-action-context.json.previous" })
        {
            string actionPath = Path.Combine(AppContext.BaseDirectory, "diagnostics", name);
            if (File.Exists(actionPath)) yield return actionPath;
        }
        string[] names = ["startup.log", "app-errors.log", "update-agent.log", "update.log", "ui-diagnostics.log"];
        foreach (string name in names)
        {
            string path = Path.Combine(AppContext.BaseDirectory, name);
            if (File.Exists(path))
            {
                yield return path;
            }
            string rootPath = Path.Combine(_installRoot(), name);
            if (!string.Equals(path, rootPath, StringComparison.OrdinalIgnoreCase) && File.Exists(rootPath))
            {
                yield return rootPath;
            }
        }
    }

    private static void WriteZipText(ZipArchive archive, string name, string text)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        using StreamWriter writer = new(entry.Open(), new UTF8Encoding(false));
        writer.Write(text);
    }
}
