using System.Text;
using IntegratedModManager.Core.DeveloperTools;
using IntegratedModManager.DeveloperTools.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using LogLevel = IntegratedModManager.Core.DeveloperTools.LogLevel;

namespace IntegratedModManager.DeveloperTools.Pages;

/// <summary>The app's logs, merged in time order, filtered by file, level, time and text; can follow new lines.</summary>
public sealed partial class LogsPage : UserControl, IToolPage
{
    private const int MaximumEntries = 2000;
    private readonly ToolContext _context;
    private readonly DispatcherTimer _followTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private List<LogEntry> _entries = [];
    private bool _filling;

    internal LogsPage(ToolContext context)
    {
        _context = context;
        InitializeComponent();
        _followTimer.Tick += async (_, _) =>
        {
            if (Visibility == Visibility.Visible) await RefreshAsync();
        };
        ApplyLanguage();
    }

    private string T(string key, params object?[] values) => _context.T(key, values);

    public void ApplyLanguage()
    {
        TitleText.Text = T("Nav.Logs");
        IntroText.Text = T("Logs.Intro");
        SearchTextBox.PlaceholderText = T("Logs.Search");
        FollowToggle.OnContent = T("Logs.Follow");
        FollowToggle.OffContent = T("Logs.Follow");
        _filling = true;
        Fill(FileComboBox, [("*", T("Logs.AllFiles")), .. LogReader.KnownLogs.Select(log => (log.Name, log.Name))]);
        Fill(LevelComboBox, [("info", T("Logs.LevelAll")), ("warning", T("Logs.LevelWarning")), ("error", T("Logs.LevelError"))]);
        Fill(SinceComboBox, [("all", T("Logs.SinceAll")), ("hour", T("Logs.SinceHour")), ("day", T("Logs.SinceDay")), ("week", T("Logs.SinceWeek"))]);
        _filling = false;
        Show();
    }

    private static void Fill(ComboBox box, IEnumerable<(string Tag, string Text)> items)
    {
        string? selected = (box.SelectedItem as ComboBoxItem)?.Tag as string;
        box.Items.Clear();
        foreach ((string tag, string text) in items) box.Items.Add(new ComboBoxItem { Content = text, Tag = tag });
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == selected) ?? box.Items[0];
    }

    public async Task RefreshAsync()
    {
        string root = _context.RootPath;
        _entries = await Task.Run(() => LogReader.Merge(LogReader.KnownLogs
            .Select(log => (IReadOnlyList<LogEntry>)LogReader.Parse(log.Name, LogReader.ReadTail(Path.Combine(root, log.Name)), log.DefaultLevel))));
        Show();
    }

    private void Show()
    {
        string file = (FileComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "*";
        LogLevel minimum = ((LevelComboBox.SelectedItem as ComboBoxItem)?.Tag as string) switch
        {
            "error" => LogLevel.Error,
            "warning" => LogLevel.Warning,
            _ => LogLevel.Info
        };
        DateTimeOffset? since = ((SinceComboBox.SelectedItem as ComboBoxItem)?.Tag as string) switch
        {
            "hour" => DateTimeOffset.Now.AddHours(-1),
            "day" => DateTimeOffset.Now.AddDays(-1),
            "week" => DateTimeOffset.Now.AddDays(-7),
            _ => null
        };
        LogEntry[] shown = [.. LogReader.Filter(_entries.Where(entry => file == "*" || entry.File == file), minimum, since, SearchTextBox.Text)];
        var text = new StringBuilder();
        foreach (LogEntry entry in shown.TakeLast(MaximumEntries))
            text.Append(entry.Time?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "                   ")
                .Append("  ").Append(entry.Level switch { LogLevel.Error => "ERROR", LogLevel.Warning => "WARN ", _ => "INFO " })
                .Append("  ").Append(entry.File).Append("  ").AppendLine(entry.Text);
        LogTextBox.Text = text.Length == 0 ? T("Logs.Empty") : text.ToString();
        CountText.Text = T("Logs.Count", shown.Length, _entries.Count);
        if (FollowToggle.IsOn) LogTextBox.Select(LogTextBox.Text.Length, 0);
    }

    private void OnFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_filling) Show();
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => Show();

    private void OnFollowToggled(object sender, RoutedEventArgs e)
    {
        if (FollowToggle.IsOn) _followTimer.Start(); else _followTimer.Stop();
        Show();
    }
}
