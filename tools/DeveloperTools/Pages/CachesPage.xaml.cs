using IntegratedModManager.Core.DeveloperTools;
using IntegratedModManager.DeveloperTools.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace IntegratedModManager.DeveloperTools.Pages;

/// <summary>
/// The app's cache folder: sizes, and clearing what the app downloads again. Clearing asks first, is refused while the
/// app runs, never follows a link, and clears the GameBanana sign-in the same way as the app's Settings card
/// (<see cref="CacheInventory.Clear"/>).
/// </summary>
public sealed partial class CachesPage : UserControl, IToolPage
{
    private readonly ToolContext _context;
    private IReadOnlyList<CacheEntry> _entries = [];
    private bool _running;

    internal CachesPage(ToolContext context)
    {
        _context = context;
        InitializeComponent();
        ApplyLanguage();
    }

    private string T(string key, params object?[] values) => _context.T(key, values);

    public void ApplyLanguage()
    {
        TitleText.Text = T("Nav.Caches");
        IntroText.Text = T("Caches.Intro");
        ListTitle.Text = T("Caches.ListTitle");
        RunningInfoBar.Message = T("Caches.AppRunning");
        Show();
    }

    public async Task RefreshAsync()
    {
        string root = _context.RootPath;
        _running = ToolContext.IsMainAppRunning();
        _entries = await Task.Run(() => CacheInventory.Read(root));
        Show();
    }

    private void Show()
    {
        RunningInfoBar.IsOpen = _running;
        CachePanel.Children.Clear();
        foreach (CacheEntry entry in _entries)
        {
            string size = !entry.Exists ? T("Caches.Empty")
                : entry.IsLink ? T("Caches.Link")
                : T("Caches.Size", ToolContext.FormatSize(entry.Bytes), entry.Files);
            string kind = T("Caches.Kind." + entry.Kind);
            bool clearable = entry.Kind != CacheKind.Kept && entry.Exists && !entry.IsLink && !_running;
            var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            right.Children.Add(PageKit.Value(size));
            if (entry.Kind != CacheKind.Kept)
            {
                Button clear = PageKit.Button(T("Caches.Clear"), async (_, _) => await ClearAsync(entry),
                    entry.Kind == CacheKind.GameBananaProfile ? "DangerButtonStyle" : "SecondaryButtonStyle");
                clear.IsEnabled = clearable;
                right.Children.Add(clear);
            }
            Button open = PageKit.Button(T("Common.OpenFolder"), (_, _) => _context.OpenFolder(entry.Path));
            open.IsEnabled = entry.Exists;
            right.Children.Add(open);
            CachePanel.Children.Add(PageKit.Row(entry.Key, kind, right));
        }
        TotalText.Text = T("Caches.Total", ToolContext.FormatSize(_entries.Where(entry => !entry.IsLink).Sum(entry => entry.Bytes)));
    }

    private async Task ClearAsync(CacheEntry entry)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme,
            Title = T("Caches.ConfirmTitle", entry.Key),
            Content = new TextBlock
            {
                Text = entry.Kind == CacheKind.GameBananaProfile ? T("Caches.ConfirmProfile") : T("Caches.ConfirmRegenerable"),
                TextWrapping = TextWrapping.Wrap
            },
            PrimaryButtonText = T("Caches.Clear"),
            CloseButtonText = T("Common.Cancel"),
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            CacheClearResult result = CacheInventory.Clear(_context.RootPath, entry.Key, ToolContext.IsMainAppRunning());
            _context.SetStatus(T("Caches.Result." + result, entry.Key),
                result == CacheClearResult.Cleared ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _context.SetStatus(T("Caches.Failed", entry.Key, ex.Message), InfoBarSeverity.Error);
        }
        await RefreshAsync();
    }
}
