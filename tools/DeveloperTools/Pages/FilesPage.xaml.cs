using System.Text.Json;
using IntegratedModManager.DeveloperTools.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace IntegratedModManager.DeveloperTools.Pages;

/// <summary>The app's settings files and logs as text: search, check JSON, copy or save a copy. Never edits them.</summary>
public sealed partial class FilesPage : UserControl, IToolPage
{
    private static readonly (string Path, string Key, bool Json, bool Binary)[] Known =
    [
        ("beta-shell.json", "Files.Shell", true, false),
        ("config.ini", "Files.Config", false, false),
        ("startup.log", "Files.StartupLog", false, false),
        ("app-errors.log", "Files.ErrorLog", false, false),
        ("update-agent.log", "Files.UpdaterLog", false, false),
        ("update.log", "Files.UpdateLog", false, false),
        (Path.Combine("cache", "app-index.db"), "Files.Index", false, true)
    ];

    private readonly ToolContext _context;
    private string _content = string.Empty;

    internal FilesPage(ToolContext context)
    {
        _context = context;
        InitializeComponent();
        ApplyLanguage();
    }

    private string T(string key, params object?[] values) => _context.T(key, values);

    public void ApplyLanguage()
    {
        TitleText.Text = T("Nav.Files");
        IntroText.Text = T("Files.Intro");
        SearchTextBox.PlaceholderText = T("Files.Search");
        ValidateButton.Content = T("Files.CheckJson");
        CopyButton.Content = T("Files.Copy");
        SaveButton.Content = T("Files.SaveAs");
    }

    public Task RefreshAsync()
    {
        int selected = FileListView.SelectedIndex;
        FileListView.Items.Clear();
        foreach ((string path, string key, bool json, bool binary) in Known)
        {
            var info = new FileInfo(Path.Combine(_context.RootPath, path));
            var item = new ListViewItem { Tag = (info.FullName, json, binary) };
            var text = new StackPanel { Spacing = 2 };
            text.Children.Add(new TextBlock { Text = path });
            text.Children.Add(new TextBlock
            {
                Text = info.Exists ? $"{T(key)} · {ToolContext.FormatSize(info.Length)}" : $"{T(key)} · {T("Common.Missing")}",
                Style = PageKit.Style("CaptionTextStyle")
            });
            item.Content = text;
            FileListView.Items.Add(item);
        }
        FileListView.SelectedIndex = selected >= 0 ? selected : 0;
        return Task.CompletedTask;
    }

    private void OnFileSelected(object sender, SelectionChangedEventArgs e)
    {
        if (FileListView.SelectedItem is not ListViewItem { Tag: (string path, bool json, bool binary) }) return;
        SearchTextBox.Text = string.Empty;
        if (!File.Exists(path)) _content = T("Common.Missing");
        else if (binary) _content = T("Files.Binary", path, ToolContext.FormatSize(new FileInfo(path).Length));
        else
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                _content = reader.ReadToEnd();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _content = T("Files.ReadFailed", ex.Message); }
        }
        ViewerTextBox.Text = _content;
        ViewerStatus.Text = path;
        ValidateButton.IsEnabled = json && File.Exists(path);
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        string query = SearchTextBox.Text.Trim();
        if (query.Length == 0)
        {
            ViewerTextBox.Text = _content;
            return;
        }
        string[] matches = [.. _content.Split('\n').Select((line, index) => (line, index))
            .Where(item => item.line.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(item => $"{item.index + 1,6}: {item.line.TrimEnd('\r')}")];
        ViewerTextBox.Text = matches.Length == 0 ? T("Files.NoMatches") : string.Join("\n", matches);
    }

    private void OnValidateClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            using JsonDocument _ = JsonDocument.Parse(_content);
            _context.SetStatus(T("Files.JsonOk"), InfoBarSeverity.Success);
        }
        catch (JsonException ex)
        {
            _context.SetStatus(T("Files.JsonBad", ex.Message), InfoBarSeverity.Error);
        }
    }

    private void OnCopyClicked(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(ViewerTextBox.Text ?? string.Empty);
        Clipboard.SetContent(package);
        _context.SetStatus(T("Files.Copied"), InfoBarSeverity.Success);
    }

    private async void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker { SuggestedFileName = "imm-file" };
        picker.FileTypeChoices.Add("Text", [".txt"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _context.WindowHandle);
        StorageFile? file = await picker.PickSaveFileAsync();
        if (file is null) return;
        await FileIO.WriteTextAsync(file, ViewerTextBox.Text ?? string.Empty);
        _context.SetStatus(T("Files.Saved"), InfoBarSeverity.Success);
    }
}
