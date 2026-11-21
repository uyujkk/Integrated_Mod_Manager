using System.Text.Json.Nodes;
using IntegratedModManager.Core.Shortcuts;
using IntegratedModManager.DeveloperTools.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace IntegratedModManager.DeveloperTools.Pages;

/// <summary>
/// For Mod authors: the key bindings the Shortcuts page and the overlay read from a Mod's INI files (the app's own
/// <see cref="ModShortcutScanner"/>), and whether each can be sent (<see cref="ShortcutKeyPlan.Explain"/>). Read-only.
/// </summary>
public sealed partial class ModKeysPage : UserControl, IToolPage
{
    private const int MaximumRows = 400;
    private readonly ToolContext _context;
    private IReadOnlyList<DetectedModShortcut> _bindings = [];
    private bool _filling;

    internal ModKeysPage(ToolContext context)
    {
        _context = context;
        InitializeComponent();
        ApplyLanguage();
    }

    private string T(string key, params object?[] values) => _context.T(key, values);

    public void ApplyLanguage()
    {
        TitleText.Text = T("Nav.ModKeys");
        IntroText.Text = T("ModKeys.Intro");
        ModComboBox.PlaceholderText = T("ModKeys.PickMod");
        FolderTextBox.PlaceholderText = T("ModKeys.Folder");
        BrowseButton.Content = T("Shell.Browse");
        ReadButton.Content = T("ModKeys.Read");
        Show();
    }

    public async Task RefreshAsync()
    {
        // The current repository's Mods (character\Mod), so a Mod can be picked without browsing.
        _filling = true;
        string? selected = (ModComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
        ModComboBox.Items.Clear();
        string? id = (string?)_context.Shell?["SelectedRepositoryId"];
        JsonNode? repository = _context.Array("Repositories").FirstOrDefault(node => (string?)node?["Id"] == id)
            ?? _context.Array("Repositories").FirstOrDefault();
        string source = (string?)repository?["SourcePath"] ?? string.Empty;
        foreach (string character in Folders(source))
            foreach (string mod in Folders(character))
                ModComboBox.Items.Add(new ComboBoxItem { Content = Path.GetFileName(character) + "\\" + Path.GetFileName(mod), Tag = mod });
        ModComboBox.SelectedItem = ModComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == selected);
        _filling = false;
        // --mod <folder> reads a Mod at once (for scripts).
        if (_bindings.Count == 0 && _context.Options.Mod is { Length: > 0 } preselected && Directory.Exists(preselected))
        {
            FolderTextBox.Text = preselected;
            await ReadAsync();
        }
    }

    private static IEnumerable<string> Folders(string path)
    {
        try { return Directory.Exists(path) ? Directory.EnumerateDirectories(path).Order(StringComparer.OrdinalIgnoreCase).Take(500).ToArray() : []; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    private async void OnModChosen(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || ModComboBox.SelectedItem is not ComboBoxItem { Tag: string folder }) return;
        FolderTextBox.Text = folder;
        await ReadAsync();
    }

    private async void OnBrowseClicked(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _context.WindowHandle);
        Windows.Storage.StorageFolder? folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;
        FolderTextBox.Text = folder.Path;
        await ReadAsync();
    }

    private async void OnReadClicked(object sender, RoutedEventArgs e) => await ReadAsync();

    private async Task ReadAsync()
    {
        string folder = FolderTextBox.Text.Trim();
        if (!Directory.Exists(folder))
        {
            _context.SetStatus(T("ModKeys.NoFolder"), InfoBarSeverity.Warning);
            return;
        }
        _bindings = await Task.Run(() => ModShortcutScanner.ScanDirectory(folder));
        Show();
    }

    private void Show()
    {
        BindingsPanel.Children.Clear();
        if (_bindings.Count == 0)
        {
            SummaryText.Text = T("ModKeys.NoneYet");
            return;
        }
        int sendable = _bindings.Count(binding => ShortcutKeyPlan.Explain(binding.RawKey) == ShortcutUnsupportedReason.None);
        SummaryText.Text = T("ModKeys.Summary", _bindings.Count, sendable, _bindings.Count - sendable);
        string folder = FolderTextBox.Text.Trim();
        foreach (DetectedModShortcut binding in _bindings.Take(MaximumRows))
        {
            ShortcutUnsupportedReason reason = ShortcutKeyPlan.Explain(binding.RawKey);
            // The scanner reports files relative to the Mod folder; show an absolute one relative to it too.
            string file = Path.IsPathRooted(binding.SourceFile) && Directory.Exists(folder)
                ? Path.GetRelativePath(folder, binding.SourceFile) : binding.SourceFile;
            var details = new List<string> { $"[{binding.Section}] · {file}" };
            if (!string.Equals(binding.RawKey, binding.Shortcut, StringComparison.Ordinal)) details.Add(T("ModKeys.Raw", binding.RawKey));
            if (!string.IsNullOrWhiteSpace(binding.Behavior)) details.Add(T("ModKeys.Behavior", binding.Behavior));
            if (!string.IsNullOrWhiteSpace(binding.Target)) details.Add(T("ModKeys.Target", binding.Target));
            if (!string.IsNullOrWhiteSpace(binding.Condition)) details.Add(T("ModKeys.Condition", binding.Condition));
            BindingsPanel.Children.Add(PageKit.Row(binding.Shortcut, string.Join("\n", details),
                PageKit.Value(reason == ShortcutUnsupportedReason.None ? T("ModKeys.Sendable") : T("ModKeys.Reason." + reason),
                    this, reason == ShortcutUnsupportedReason.None ? "AppSuccessBrush" : "AppDangerBrush")));
        }
        if (_bindings.Count > MaximumRows)
            BindingsPanel.Children.Add(new TextBlock { Text = T("Common.MoreRows", _bindings.Count - MaximumRows), Style = PageKit.Style("CaptionTextStyle") });
    }
}
