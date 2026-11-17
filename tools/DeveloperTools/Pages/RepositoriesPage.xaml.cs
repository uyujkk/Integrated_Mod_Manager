using System.Text;
using System.Text.Json.Nodes;
using IntegratedModManager.Core.Deployment;
using IntegratedModManager.DeveloperTools.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace IntegratedModManager.DeveloperTools.Pages;

/// <summary>Each repository's folders, launcher and link mode, checked against the disk.</summary>
public sealed partial class RepositoriesPage : UserControl, IToolPage
{
    private readonly ToolContext _context;
    private bool _filling;

    internal RepositoriesPage(ToolContext context)
    {
        _context = context;
        InitializeComponent();
        ApplyLanguage();
    }

    private string T(string key, params object?[] values) => _context.T(key, values);

    public void ApplyLanguage()
    {
        TitleText.Text = T("Nav.Repositories");
        IntroText.Text = T("Repositories.Intro");
        RepositoryComboBox.PlaceholderText = T("Repositories.None");
        OpenSourceButton.Content = T("Repositories.OpenSource");
        OpenTargetButton.Content = T("Repositories.OpenTarget");
        PathsTitle.Text = T("Repositories.Paths");
        ReportTitle.Text = T("Repositories.Report");
    }

    public Task RefreshAsync()
    {
        string? selected = (RepositoryComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? (string?)_context.Shell?["SelectedRepositoryId"];
        _filling = true;
        RepositoryComboBox.Items.Clear();
        foreach (JsonNode? repository in _context.Array("Repositories"))
        {
            string id = (string?)repository?["Id"] ?? string.Empty;
            string name = (string?)repository?["Name"] ?? id;
            RepositoryComboBox.Items.Add(new ComboBoxItem { Content = name, Tag = id });
        }
        RepositoryComboBox.SelectedItem = RepositoryComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == selected)
            ?? RepositoryComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
        _filling = false;
        ShowSelected();
        return Task.CompletedTask;
    }

    private JsonNode? Selected()
    {
        string? id = (RepositoryComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
        return _context.Array("Repositories").FirstOrDefault(repository => (string?)repository?["Id"] == id);
    }

    private void OnRepositoryChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_filling) ShowSelected();
    }

    private void ShowSelected()
    {
        PathsPanel.Children.Clear();
        JsonNode? repository = Selected();
        if (repository is null)
        {
            ReportTextBox.Text = T("Repositories.NoneInSettings");
            return;
        }
        string source = (string?)repository["SourcePath"] ?? string.Empty;
        string target = (string?)repository["TargetPath"] ?? string.Empty;
        string launcher = (string?)repository["LauncherPath"] ?? string.Empty;
        AddPath(T("Repositories.Source"), source, Directory.Exists(source));
        AddPath(T("Repositories.Target"), target, Directory.Exists(target));
        AddPath(T("Repositories.Launcher"), launcher, File.Exists(launcher));
        AddPath(T("Repositories.UserIni"), (string?)repository["PersistentUserIniPath"] ?? string.Empty,
            File.Exists((string?)repository["PersistentUserIniPath"] ?? string.Empty));

        bool links = (bool?)repository["UseDirectoryLinks"] == true;
        int sourceFolders = Count(source, _ => true), targetFolders = Count(target, _ => true);
        int linked = Count(target, DirectoryLinkDeployment.IsDirectoryLink);
        var report = new StringBuilder();
        report.AppendLine($"{T("Repositories.Name")}: {(string?)repository["Name"]}");
        report.AppendLine($"ID: {(string?)repository["Id"]}");
        report.AppendLine($"{T("Repositories.Mode")}: {(links ? T("Repositories.ModeLinks") : T("Repositories.ModeCopy"))}");
        report.AppendLine($"{T("Repositories.Online")}: {(string?)repository["OnlineSourceSite"]} · {(string?)repository["OnlineCategoryId"]} · {(string?)repository["OnlineGameName"]}");
        report.AppendLine();
        report.AppendLine(T("Repositories.SourceFolders", sourceFolders));
        report.AppendLine(T("Repositories.TargetFolders", targetFolders, linked));
        report.AppendLine(!links
            ? (linked == 0 ? T("Repositories.CopyModeOk") : T("Repositories.CopyModeHasLinks", linked))
            : targetFolders == 0 ? T("Repositories.TargetEmpty") : T("Repositories.LinkModeCount", linked, targetFolders));
        ReportTextBox.Text = report.ToString();
    }

    private void AddPath(string label, string path, bool exists)
    {
        PathsPanel.Children.Add(PageKit.Row(label, string.IsNullOrWhiteSpace(path) ? T("Repositories.NotSet") : path,
            PageKit.Value(exists ? T("Common.Found") : T("Common.Missing"), this, exists ? "AppSuccessBrush" : "AppDangerBrush")));
    }

    private static int Count(string folder, Func<string, bool> match)
    {
        try { return Directory.Exists(folder) ? Directory.EnumerateDirectories(folder).Count(match) : 0; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }

    private void OnOpenSourceClicked(object sender, RoutedEventArgs e)
    {
        if ((string?)Selected()?["SourcePath"] is { Length: > 0 } path) _context.OpenFolder(path);
    }

    private void OnOpenTargetClicked(object sender, RoutedEventArgs e)
    {
        if ((string?)Selected()?["TargetPath"] is { Length: > 0 } path) _context.OpenFolder(path);
    }
}
