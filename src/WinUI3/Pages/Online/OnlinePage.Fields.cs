using IntegratedModManager.Core.Online;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ModFolderCopier.WinUI;

// State and helpers only the Online page uses, moved from the window's files (stage J6).
public sealed partial class OnlinePage
{
    // A selectable button drawn like a toggle: the online page's list and grid buttons.
    internal void ApplySecondaryNavButtonVisual(Button button, bool isSelected)
    {
        Brush backgroundBrush = isSelected
            ? GetAppThemeBrush("AppSecondarySelectedBrush")
            : GetAppThemeBrush("AppSecondaryDefaultBrush");
        Brush borderBrush = isSelected
            ? GetAppThemeBrush("AppNavSelectedBorderBrush")
            : GetAppThemeBrush("AppCardBorderBrush");
        Brush foregroundBrush = isSelected
            ? GetAppThemeBrush("AppSecondarySelectedForegroundBrush")
            : GetAppThemeBrush("AppSecondaryDefaultForegroundBrush");
        Brush disabledForegroundBrush = GetAppThemeBrush("AppDisabledForegroundBrush");
        Brush disabledBackgroundBrush = GetAppThemeBrush("AppSecondaryDefaultBrush");
        Brush disabledBorderBrush = GetAppThemeBrush("AppCardBorderBrush");

        button.Background = backgroundBrush;
        button.BorderBrush = borderBrush;
        button.Foreground = foregroundBrush;

        button.Resources["ButtonBackground"] = backgroundBrush;
        button.Resources["ButtonBackgroundPointerOver"] = backgroundBrush;
        button.Resources["ButtonBackgroundPressed"] = backgroundBrush;
        button.Resources["ButtonBackgroundDisabled"] = disabledBackgroundBrush;

        button.Resources["ButtonBorderBrush"] = borderBrush;
        button.Resources["ButtonBorderBrushPointerOver"] = borderBrush;
        button.Resources["ButtonBorderBrushPressed"] = borderBrush;
        button.Resources["ButtonBorderBrushDisabled"] = disabledBorderBrush;

        button.Resources["ButtonForeground"] = foregroundBrush;
        button.Resources["ButtonForegroundPointerOver"] = foregroundBrush;
        button.Resources["ButtonForegroundPressed"] = foregroundBrush;
        button.Resources["ButtonForegroundDisabled"] = disabledForegroundBrush;
    }
    // Tasks restored from the history file have only the stored Chinese and English status.
    internal string DownloadTaskStatusText(DownloadTaskItem task) => DownloadTaskViews.StatusText(task, _services.Language);
    internal static readonly HashSet<string> GenericOnlineCharacterNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "operator", "operators", "character", "characters", "skin", "skins",
        "other", "others", "misc", "miscellaneous", "uncategorized", "unclassified"
    };
    /// <summary>A folder the user may choose to have created (issue #6); created only after confirmation.</summary>
    internal sealed record NewCharacterFolderChoice(string Path);
    internal static string NormalizeShortcut(string? shortcut)
    {
        return string.Join("+", (shortcut ?? string.Empty)
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part =>
            {
                if (string.Equals(part, "control", StringComparison.OrdinalIgnoreCase))
                {
                    return "Ctrl";
                }

                if (string.Equals(part, "menu", StringComparison.OrdinalIgnoreCase))
                {
                    return "Alt";
                }

                return part.Length == 1 ? part.ToUpperInvariant() : char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant();
            }));
    }
    internal const int OnlineDisplayPageSize = 40;
    // How long a page may wait for its download counts before it is shown without them.
    internal static readonly TimeSpan OnlineDownloadCountInlineBudget = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan OnlineModListRequestTimeout = TimeSpan.FromSeconds(5);
    internal const int OnlineRawPageLimit = 80;
    internal void TraceOnlineUiState(string stage, int itemId = 0)
    {
        if (_onlineWindowClosed) return;
        try
        {
            if (stage is "Details requested" or "Pane opening" or "Details publishing" or "Hero assigning decoded bitmap")
                UiCrashDiagnostics.RecordAction(stage, itemId, _host.Root?.ActualWidth ?? 0, _host.Root?.ActualHeight ?? 0);
            if (!UiCrashDiagnostics.Enabled) return;
            UiCrashDiagnostics.Write(stage,
            $"item={itemId} root={_host.Root?.ActualWidth:F2}x{_host.Root?.ActualHeight:F2} " +
            $"host={OnlineDetailsHost?.ActualWidth:F2} drawer={OnlineDetailDrawer?.Visibility} " +
            $"drawerWidth={OnlineDetailDrawer?.Width:F2} " +
            $"workspace={OnlineWorkspaceGrid?.ActualWidth:F2}x{OnlineWorkspaceGrid?.ActualHeight:F2} " +
            $"results={OnlineResultsHost?.ActualWidth:F2}x{OnlineResultsHost?.ActualHeight:F2} " +
            $"cards={_onlinePage.CardLayoutMode} closed={_onlineWindowClosed}");
        }
        catch { /* A disconnected XAML object must not turn tracing into a failure. */ }
    }
    internal FrameworkElement WrapCharacterButtonWithFavorite(Button characterButton, string characterKey)
    {
        if (string.IsNullOrWhiteSpace(characterKey))
        {
            return characterButton;
        }

        bool isFavorite = _host.IsFavoriteCharacter(characterKey);
        var host = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        host.Children.Add(characterButton);

        var favoriteButton = new Button
        {
            Width = _useHorizontalOnlineCharacterRail ? 26 : 30,
            Height = _useHorizontalOnlineCharacterRail ? 26 : 30,
            Padding = new Thickness(0),
            Margin = _useHorizontalOnlineCharacterRail
                ? new Thickness(0, 4, 4, 0)
                : new Thickness(0, 0, 7, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = _useHorizontalOnlineCharacterRail
                ? VerticalAlignment.Top
                : VerticalAlignment.Center,
            CornerRadius = new CornerRadius(15),
            Background = GetAppThemeBrush(isFavorite ? "AppAccentSoftBrush" : "AppSecondaryDefaultBrush"),
            BorderBrush = GetAppThemeBrush(isFavorite ? "AppNavSelectedBorderBrush" : "AppCardBorderBrush"),
            BorderThickness = new Thickness(1),
            Foreground = GetAppThemeBrush(isFavorite ? "AppAccentBrush" : "AppSecondaryDefaultForegroundBrush"),
            Content = new FontIcon
            {
                Glyph = isFavorite ? "\uE735" : "\uE734",
                FontSize = _useHorizontalOnlineCharacterRail ? 12 : 14
            }
        };
        string accessibleName = isFavorite
            ? T("ProductivityFeatures.RemoveCharacterFromFavorites")
            : T("ProductivityFeatures.FavoriteAndPinCharacter");
        AutomationProperties.SetName(favoriteButton, accessibleName);
        ToolTipService.SetToolTip(favoriteButton, accessibleName);
        favoriteButton.Click += (_, args) =>
        {
            _host.ToggleFavoriteCharacter(characterKey);
        };
        host.Children.Add(favoriteButton);
        return host;
    }
    internal OnlineModCard? _activeOnlineDetailMod;
    internal bool _animateOnlineCardsOnNextRefresh;
    internal string? _blockedOnlineAutoLoadConfigKey;
    internal string? _foregroundDownloadTaskId;
    internal bool _isApplyingOnlineCharacterSelection;
    internal bool _isLoadingOnlineMods;
    internal string? _lastLoadedOnlineConfigKey;
    internal readonly HashSet<string> _loadingOnlineCharacterCatalogKeys = new(StringComparer.Ordinal);
    internal CancellationTokenSource _onlineCharacterAvatarCancellation = new();
    internal readonly SemaphoreSlim _onlineCharacterAvatarDownloadGate = new(3, 3);
    internal int _onlineCharacterAvatarRequestVersion;
    internal readonly string _onlineCharacterCachePath = Path.Combine(AppContext.BaseDirectory, "cache", "online-characters");
    internal string? _onlineCharacterCatalogKey;
    internal string? _onlineCharacterStripSignature;
    internal int _onlineDetailAnimationVersion;
    internal int _onlineDetailImageIndex;
    internal readonly List<string> _onlineDetailImageUrls = [];
    internal static readonly string _onlineDetailsCachePath = Path.Combine(AppContext.BaseDirectory, "cache", "online-details");
    internal int _onlineDownloadEnrichmentVersion;
    internal int _onlineHeroImageRequestVersion;
    internal CancellationTokenSource? _onlineModListCancellation;
    internal int _onlineModRequestVersion;
    internal readonly string _onlinePageCachePath = Path.Combine(AppContext.BaseDirectory, "cache", "online-pages");
    internal readonly RequestCooldown _onlineRateLimit = new();
    internal bool _useHorizontalOnlineCharacterRail;
}
