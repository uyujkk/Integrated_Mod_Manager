using System.Runtime.InteropServices;
using IntegratedModManager.Data;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    // The logs are AppLog's (stage J finish, step 8a).
    private static void TraceStartupStage(string stage) => AppLog.TraceStartupStage(stage);

    private static void LogPersistentDataStoreIssue(string operation, Exception exception) => AppLog.LogPersistentDataStoreIssue(operation, exception);

    internal static void LogApplicationIssue(string operation, Exception exception) => AppLog.LogApplicationIssue(operation, exception);

    internal static void LogApplicationIssueWithStack(string operation, Exception exception) => AppLog.LogApplicationIssueWithStack(operation, exception);

    private readonly AppDataStore _appDataStore;
    private readonly HashSet<string> _favoriteCharacterKeys = new(StringComparer.OrdinalIgnoreCase);
    private bool _systemHighContrast;
    private bool _systemAnimationsEnabled = true;
    private bool _isApplyingStartupUpdateSetting;
    private bool _isApplyingBackupLimitSetting;

    private const uint SpiGetHighContrast = 0x0042;
    private const uint SpiGetClientAreaAnimation = 0x1042;
    private const uint HcfHighContrastOn = 0x00000001;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct HighContrastInfo
    {
        public uint Size;
        public uint Flags;
        public IntPtr DefaultScheme;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(
        uint action,
        uint parameter,
        IntPtr value,
        uint flags);

    private void InitializePersistentDataStore()
    {
        _appDataStore.Initialize();
        foreach (string favorite in _appDataStore.ReadFavorites())
        {
            _favoriteCharacterKeys.Add(favorite);
        }
    }

    private bool ShouldReduceAnimations => _shell.ReduceMotion
        || _systemHighContrast
        || !_systemAnimationsEnabled;

    private void InitializeAccessibilityFeatures()
    {
        RootGrid.UseSystemFocusVisuals = true;
        AutomationProperties.SetName(RootGrid, T("ProductivityFeatures.IntegratedModManagerMainWindow"));
        RefreshSystemAccessibilityPreferences();
        RootGrid.ActualThemeChanged += OnRootActualThemeChanged;
    }

    private void OnRootActualThemeChanged(FrameworkElement sender, object args)
    {
        RefreshSystemAccessibilityPreferences();
    }

    private void RefreshSystemAccessibilityPreferences()
    {
        _systemHighContrast = ReadHighContrastState();
        _services.Appearance.IsHighContrast = _systemHighContrast;
        _systemAnimationsEnabled = ReadClientAreaAnimationState();
    }

    private static bool ReadHighContrastState()
    {
        HighContrastInfo info = new()
        {
            Size = (uint)Marshal.SizeOf<HighContrastInfo>()
        };
        IntPtr buffer = Marshal.AllocHGlobal(Marshal.SizeOf<HighContrastInfo>());
        try
        {
            Marshal.StructureToPtr(info, buffer, false);
            return SystemParametersInfo(SpiGetHighContrast, info.Size, buffer, 0)
                && (Marshal.PtrToStructure<HighContrastInfo>(buffer).Flags & HcfHighContrastOn) != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool ReadClientAreaAnimationState()
    {
        IntPtr buffer = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Marshal.WriteInt32(buffer, 1);
            return !SystemParametersInfo(SpiGetClientAreaAnimation, 0, buffer, 0)
                || Marshal.ReadInt32(buffer) != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void NavigateToPrimarySection(PrimarySection section)
    {
        _shell.CurrentSection = section;
        ApplyShellState(refreshRepository: section == PrimarySection.Repository);
        SaveShellConfig();
    }

    private bool IsFavoriteCharacter(string characterKey)
    {
        if (string.IsNullOrWhiteSpace(characterKey) || string.IsNullOrWhiteSpace(_services.Online.CharacterCatalogKey))
        {
            return false;
        }

        return _favoriteCharacterKeys.Contains(AppDataStore.BuildFavoriteKey(_services.Online.CharacterCatalogKey, characterKey));
    }

    private void ToggleFavoriteCharacter(string characterKey)
    {
        if (string.IsNullOrWhiteSpace(characterKey) || string.IsNullOrWhiteSpace(_services.Online.CharacterCatalogKey))
        {
            return;
        }

        string favoriteKey = AppDataStore.BuildFavoriteKey(_services.Online.CharacterCatalogKey, characterKey);
        bool isFavorite = !_favoriteCharacterKeys.Contains(favoriteKey);
        if (isFavorite)
        {
            _favoriteCharacterKeys.Add(favoriteKey);
        }
        else
        {
            _favoriteCharacterKeys.Remove(favoriteKey);
        }

        _appDataStore.SetFavorite(_services.Online.CharacterCatalogKey, characterKey, isFavorite);
        if (_onlineView is null) EnsureOnlineCharacterOptions(_services.Repositories.Selected);
        else _onlineView.RefreshFavoriteCharacters();
        ShowAppNotification(
                isFavorite ? T("ProductivityFeatures.CharacterFavorited") : T("ProductivityFeatures.CharacterUnfavorited"),
                InfoBarSeverity.Informational);
    }

    private void RefreshBackupManagerText()
    {
        _isApplyingBackupLimitSetting = true;
        UpdatesView.BackupLimitGb = _shell.InstallBackupLimitGb;
        _isApplyingBackupLimitSetting = false;
    }

    private async void OnRefreshBackupsClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            await _services.Install.RefreshBackupListAsync();
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnRefreshBackupsClicked", ex);
        }
    }

    private void AnimateElementEntrance(UIElement element)
    {
        if (ShouldReduceAnimations)
        {
            return;
        }

        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(element);
        visual.Opacity = 0;
        visual.Offset = new System.Numerics.Vector3(0, 8, 0);
        var compositor = visual.Compositor;
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.Duration = TimeSpan.FromMilliseconds(150);
        fade.InsertKeyFrame(1, 1);
        var slide = compositor.CreateVector3KeyFrameAnimation();
        slide.Duration = TimeSpan.FromMilliseconds(180);
        slide.InsertKeyFrame(1, System.Numerics.Vector3.Zero);
        visual.StartAnimation("Opacity", fade);
        visual.StartAnimation("Offset", slide);
    }

}
