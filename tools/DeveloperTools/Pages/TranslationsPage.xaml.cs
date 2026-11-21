using System.Text.Json;
using IntegratedModManager.Core.DeveloperTools;
using IntegratedModManager.DeveloperTools.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace IntegratedModManager.DeveloperTools.Pages;

/// <summary>
/// For translators: every language file in a <c>Strings</c> folder compared with a reference language (missing and extra
/// keys, placeholders the app cannot fill, empty texts). The package embeds the app's texts, so this reads a source
/// checkout's <c>src\WinUI3\Strings</c> or any folder of language files. Read-only.
/// </summary>
public sealed partial class TranslationsPage : UserControl, IToolPage
{
    private const int MaximumRows = 400;
    private readonly ToolContext _context;
    private Dictionary<string, Dictionary<string, string>> _languages = [];
    private LanguageComparison? _comparison;
    private bool _filling;

    internal TranslationsPage(ToolContext context)
    {
        _context = context;
        InitializeComponent();
        FolderTextBox.Text = _context.Settings.StringsFolder is { Length: > 0 } saved && Directory.Exists(saved) ? saved
            : ToolContext.FindSourceStrings() ?? string.Empty;
        ApplyLanguage();
    }

    private string T(string key, params object?[] values) => _context.T(key, values);

    public void ApplyLanguage()
    {
        TitleText.Text = T("Nav.Translations");
        IntroText.Text = T("Translations.Intro");
        FolderTextBox.PlaceholderText = T("Translations.Folder");
        BrowseButton.Content = T("Shell.Browse");
        CompareButton.Content = T("Translations.Compare");
        ReferenceComboBox.PlaceholderText = T("Translations.Reference");
        IssuesTitle.Text = T("Translations.Issues");
        FillFilters();
        Show();
    }

    public async Task RefreshAsync()
    {
        if (Directory.Exists(FolderTextBox.Text)) await CompareAsync();
    }

    private async void OnBrowseClicked(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _context.WindowHandle);
        Windows.Storage.StorageFolder? folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;
        FolderTextBox.Text = folder.Path;
        _context.Settings.StringsFolder = folder.Path;
        _context.SaveSettings();
        await CompareAsync();
    }

    private async void OnCompareClicked(object sender, RoutedEventArgs e) => await CompareAsync();

    private async void OnReferenceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_filling) await CompareAsync(reload: false);
    }

    private void OnFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_filling) Show();
    }

    private async Task CompareAsync(bool reload = true)
    {
        string folder = FolderTextBox.Text.Trim();
        if (!Directory.Exists(folder))
        {
            _context.SetStatus(T("Translations.NoFolder"), InfoBarSeverity.Warning);
            return;
        }
        try
        {
            if (reload || _languages.Count == 0) _languages = await Task.Run(() => LanguageFileComparer.ReadFolder(folder));
            if (_languages.Count == 0)
            {
                _context.SetStatus(T("Translations.NoFiles"), InfoBarSeverity.Warning);
                return;
            }
            _filling = true;
            string reference = (ReferenceComboBox.SelectedItem as ComboBoxItem)?.Tag as string
                ?? (_languages.ContainsKey(LanguageFileComparer.DefaultReference) ? LanguageFileComparer.DefaultReference : _languages.Keys.First());
            if (!_languages.ContainsKey(reference)) reference = _languages.Keys.First();
            ReferenceComboBox.Items.Clear();
            foreach (string code in _languages.Keys) ReferenceComboBox.Items.Add(new ComboBoxItem { Content = code, Tag = code });
            ReferenceComboBox.SelectedItem = ReferenceComboBox.Items.OfType<ComboBoxItem>().First(item => (string)item.Tag == reference);
            _filling = false;
            _comparison = LanguageFileComparer.Compare(_languages, reference);
            FillFilters();
            Show();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            _filling = false;
            _context.SetStatus(T("Translations.ReadFailed", ex.Message), InfoBarSeverity.Error);
        }
    }

    private void FillFilters()
    {
        _filling = true;
        string? language = (LanguageFilter.SelectedItem as ComboBoxItem)?.Tag as string;
        string? kind = (KindFilter.SelectedItem as ComboBoxItem)?.Tag as string;
        LanguageFilter.Items.Clear();
        LanguageFilter.Items.Add(new ComboBoxItem { Content = T("Translations.AllLanguages"), Tag = "*" });
        foreach (string code in _languages.Keys) LanguageFilter.Items.Add(new ComboBoxItem { Content = code, Tag = code });
        LanguageFilter.SelectedItem = LanguageFilter.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == language) ?? LanguageFilter.Items[0];
        KindFilter.Items.Clear();
        KindFilter.Items.Add(new ComboBoxItem { Content = T("Translations.AllKinds"), Tag = "*" });
        foreach (LanguageIssueKind value in Enum.GetValues<LanguageIssueKind>())
            KindFilter.Items.Add(new ComboBoxItem { Content = T("Translations.Kind." + value), Tag = value.ToString() });
        KindFilter.SelectedItem = KindFilter.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == kind) ?? KindFilter.Items[0];
        _filling = false;
    }

    private void Show()
    {
        SummaryPanel.Children.Clear();
        IssuesPanel.Children.Clear();
        if (_comparison is null)
        {
            IssuesPanel.Children.Add(new TextBlock { Text = T("Translations.NotYet"), Style = PageKit.Style("MutedTextStyle"), TextWrapping = TextWrapping.Wrap });
            return;
        }
        foreach (LanguageFileSummary file in _comparison.Files)
        {
            bool reference = file.Language == _comparison.Reference;
            bool clean = file.Missing + file.Extra + file.Placeholders + file.Empty == 0;
            SummaryPanel.Children.Add(PageKit.Row(reference ? T("Translations.ReferenceRow", file.Language) : file.Language,
                T("Translations.FileSummary", file.Keys, file.Missing, file.Extra, file.Placeholders, file.Empty),
                PageKit.Value(clean ? T("Translations.Complete") : T("Translations.NeedsWork"), this, clean ? "AppSuccessBrush" : "AppDangerBrush")));
        }
        string language = (LanguageFilter.SelectedItem as ComboBoxItem)?.Tag as string ?? "*";
        string kind = (KindFilter.SelectedItem as ComboBoxItem)?.Tag as string ?? "*";
        LanguageIssue[] issues = [.. _comparison.Issues.Where(issue => (language == "*" || issue.Language == language) && (kind == "*" || issue.Kind.ToString() == kind))];
        foreach (LanguageIssue issue in issues.Take(MaximumRows))
            IssuesPanel.Children.Add(PageKit.Row($"{issue.Key}", $"{issue.Language} · {T("Translations.Kind." + issue.Kind)}" + (string.IsNullOrWhiteSpace(issue.Detail) ? "" : "\n" + issue.Detail), null));
        if (issues.Length == 0)
            IssuesPanel.Children.Add(new TextBlock { Text = T("Translations.NoIssues"), Style = PageKit.Style("MutedTextStyle") });
        else if (issues.Length > MaximumRows)
            IssuesPanel.Children.Add(new TextBlock { Text = T("Common.MoreRows", issues.Length - MaximumRows), Style = PageKit.Style("CaptionTextStyle") });
    }
}
