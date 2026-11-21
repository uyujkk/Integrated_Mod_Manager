using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using IntegratedModManager.DeveloperTools.Services;
using Microsoft.UI.Xaml.Controls;

namespace IntegratedModManager.DeveloperTools.Pages;

/// <summary>The app's version, repositories, storage and key files at a glance.</summary>
public sealed partial class OverviewPage : UserControl, IToolPage
{
    private readonly ToolContext _context;

    internal OverviewPage(ToolContext context)
    {
        _context = context;
        InitializeComponent();
        ApplyLanguage();
    }

    private string T(string key, params object?[] values) => _context.T(key, values);

    public void ApplyLanguage()
    {
        TitleText.Text = T("Nav.Overview");
        IntroText.Text = T("Overview.Intro");
        VersionLabel.Text = T("Overview.Version");
        RepositoriesLabel.Text = T("Overview.Repositories");
        StorageLabel.Text = T("Overview.Storage");
        FilesLabel.Text = T("Overview.KeyFiles");
        RuntimeTitle.Text = T("Overview.Runtime");
        BreakdownTitle.Text = T("Overview.Breakdown");
    }

    public async Task RefreshAsync()
    {
        string root = _context.RootPath;
        string executable = Path.Combine(root, "ModFolderCopier.WinUI.exe");
        VersionValue.Text = File.Exists(executable) ? FileVersionInfo.GetVersionInfo(executable).ProductVersion ?? "-" : "-";
        RepositoriesValue.Text = _context.Array("Repositories").Count.ToString(System.Globalization.CultureInfo.CurrentCulture);
        string[] keyFiles = ["ModFolderCopier.WinUI.exe", "beta-shell.json", "config.ini"];
        FilesValue.Text = $"{keyFiles.Count(name => File.Exists(Path.Combine(root, name)))}/{keyFiles.Length}";

        bool running = ToolContext.IsMainAppRunning();
        RunningInfoBar.IsOpen = true;
        RunningInfoBar.Severity = running ? Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning : Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational;
        RunningInfoBar.Message = running ? T("Overview.AppRunning") : T("Overview.AppClosed");

        (string Key, long Bytes)[] parts = await Task.Run(() => new[]
        {
            ("Overview.Cache", FolderSize(Path.Combine(root, "cache"))),
            ("Overview.Backups", FolderSize(Path.Combine(root, "backups"))),
            ("Overview.Snapshots", FolderSize(Path.Combine(root, "snapshots"))),
            ("Overview.Diagnostics", FolderSize(Path.Combine(root, "diagnostics"))),
            ("Overview.Settings", FileSize(Path.Combine(root, "beta-shell.json")) + FileSize(Path.Combine(root, "config.ini")))
        });
        StorageValue.Text = ToolContext.FormatSize(parts.Sum(part => part.Bytes));
        BreakdownPanel.Children.Clear();
        foreach ((string key, long bytes) in parts)
            BreakdownPanel.Children.Add(PageKit.Row(T(key), null, PageKit.Value(ToolContext.FormatSize(bytes))));

        var runtime = new StringBuilder();
        runtime.AppendLine($"{T("Overview.ToolsVersion")}: {typeof(OverviewPage).Assembly.GetName().Version}");
        runtime.AppendLine($"{T("Shell.AppFolder")}: {root}");
        runtime.AppendLine($"{T("Overview.AppProcess")}: {(running ? T("Overview.Running") : T("Overview.NotRunning"))}");
        runtime.AppendLine($"{T("Overview.System")}: {RuntimeInformation.OSDescription}");
        runtime.AppendLine($"{T("Overview.DotNet")}: {RuntimeInformation.FrameworkDescription} · {RuntimeInformation.ProcessArchitecture}");
        runtime.AppendLine();
        foreach (string file in new[] { "beta-shell.json", "config.ini", Path.Combine("cache", "app-index.db"), "startup.log", "app-errors.log" })
            runtime.AppendLine($"{file}: {FileState(Path.Combine(root, file))}");
        RuntimeTextBox.Text = runtime.ToString();
    }

    private string FileState(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? $"{ToolContext.FormatSize(info.Length)} · {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}" : T("Common.Missing");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return T("Common.Unavailable"); }
    }

    private static long FileSize(string path)
    {
        try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }

    // Links are not followed (the backups may hold junctions to Mod folders).
    private static long FolderSize(string path)
    {
        if (!Directory.Exists(path)) return 0;
        long total = 0;
        var pending = new Stack<string>();
        pending.Push(path);
        while (pending.Count > 0)
        {
            string folder = pending.Pop();
            IEnumerable<string> entries;
            try { entries = Directory.EnumerateFileSystemEntries(folder).ToArray(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (string entry in entries)
            {
                try
                {
                    FileAttributes attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
                    else total += new FileInfo(entry).Length;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        return total;
    }
}
