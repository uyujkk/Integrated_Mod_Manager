using System.Numerics;
using IntegratedModManager.Core.Online;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private void RegisterTrackedTextInputs()
    {
        // The dashboard's path boxes are on its page (stage J4).
        foreach (TextBox box in ((DashboardPage)_pages[PrimarySection.Dashboard]).PathTextBoxes)
        {
            AttachTrackedTextInput(box);
        }
        AttachTrackedTextInput(FirstLevelSearchTextBox);
        AttachTrackedTextInput(ModLinkTextBox);
    }

    private void AttachTrackedTextInput(TextBox box)
    {
        box.GotFocus += OnTrackedTextBoxGotFocus;
        box.LostFocus += OnTrackedTextBoxLostFocus;
    }

    private void OnTrackedTextBoxGotFocus(object sender, RoutedEventArgs e)
    {
        _lastFocusedTextBox = sender as TextBox;
    }

    private void OnTrackedTextBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(_lastFocusedTextBox, sender))
        {
            _lastFocusedTextBox = null;
        }
    }

    // A selectable button drawn like a toggle: the online page's list and grid buttons.
    private void ApplySecondaryNavButtonVisual(Button button, bool isSelected)
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

    // Set by a page switch whose refreshes have not run yet, so a quick second switch still refreshes the lists.
    private bool _pendingRepositoryRefresh;
    private int _shellStateVersion;

    private void ApplyShellState(bool refreshRepository)
    {
        // What the click must show at once: the page and the navigation's selection, in this frame. The refreshes
        // below used to run first and held the selection back by 50-100 ms on every switch (J8 follow-up).
        PageContentHost.Content = _pages[_currentPrimarySection];
        RefreshPrimaryNavigationVisuals();
        if (_currentPrimarySection == PrimarySection.Shortcuts)
        {
            _shortcutsPage!.EnsureBuilt();
            PrepareShortcuts();
            _ = RefreshShortcutPaneSafelyAsync();
        }

        UpdateShellLayout();
        AnimateSectionEntrance(GetActiveSectionElement());
        RootGrid.Focus(FocusState.Programmatic);

        // The rest runs right after that frame; a newer switch replaces a refresh that has not run yet.
        _pendingRepositoryRefresh |= refreshRepository;
        int version = ++_shellStateVersion;
        RootGrid.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (version != _shellStateVersion || _onlineWindowClosed) return;
            RefreshShellStateAfterSwitch();
        });
    }

    private void RefreshShellStateAfterSwitch()
    {
        ApplyLanguage();
        RefreshSettingsPane();

        if (_currentPrimarySection is PrimarySection.Dashboard or PrimarySection.Repository or PrimarySection.Online)
        {
            ApplySelectedRepositoryToInputs();
        }

        if (_pendingRepositoryRefresh && _currentPrimarySection == PrimarySection.Repository)
        {
            _pendingRepositoryRefresh = false;
            _ = RefreshListsAsync();
        }

        RefreshDashboard();
        RefreshOnlinePaneV2();
        RefreshUpdatesPane();
        RefreshRepositoryActionButtons();
        SaveShellConfig();
    }

    // Each page container stays alive in its wrapper, so switching keeps scroll positions,
    // selections and loading work, as when the hidden pages were only collapsed.
    private readonly Dictionary<PrimarySection, UserControl> _pages = [];

    private void InitializePageHost()
    {
        foreach ((PrimarySection section, FrameworkElement container) in new (PrimarySection, FrameworkElement)[]
        {
            (PrimarySection.Repository, WorkspacePageHost),
            (PrimarySection.Online, OnlineScrollViewer)
        })
        {
            PagesHostGrid.Children.Remove(container);
            _pages[section] = new WrapperPage(container);
        }
        // Stage J2: the Settings page is a page of its own.
        _pages[PrimarySection.Settings] = new SettingsPage(_services);
        // Stage J4: the dashboard.
        _pages[PrimarySection.Dashboard] = new DashboardPage(_services);
        // Stage J3: the Updates page too.
        _pages[PrimarySection.Updates] = new UpdatesPage(_services);
        // Stage J5: the Shortcuts page.
        _pages[PrimarySection.Shortcuts] = _shortcutsPage = new ShortcutsPage(_services);
    }

    private UIElement GetActiveSectionElement()
    {
        return _currentPrimarySection switch
        {
            PrimarySection.Dashboard => _pages[PrimarySection.Dashboard],
            PrimarySection.Repository => WorkspacePageHost,
            PrimarySection.Online => OnlineScrollViewer,
            PrimarySection.Updates => _pages[PrimarySection.Updates],
            PrimarySection.Shortcuts => _pages[PrimarySection.Shortcuts],
            _ => _pages[PrimarySection.Settings]
        };
    }

    private void AnimateSectionEntrance(UIElement element)
    {
        if (_reduceMotion || element.Visibility != Visibility.Visible)
        {
            return;
        }

        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;
        var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1f), new Vector2(0.3f, 1f));

        // A light entrance that starts mostly visible, so the page is there at once (was 25 % opacity, 12 px and
        // 170/210 ms, which read as lag; J8 follow-up). Reduce motion turns it off.
        visual.Opacity = 0.7f;
        visual.Offset = new Vector3(6f, 0f, 0f);

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.Duration = TimeSpan.FromMilliseconds(110);
        fade.InsertKeyFrame(1f, 1f, easing);

        var slide = compositor.CreateVector3KeyFrameAnimation();
        slide.Duration = TimeSpan.FromMilliseconds(130);
        slide.InsertKeyFrame(1f, Vector3.Zero, easing);

        visual.StartAnimation("Opacity", fade);
        visual.StartAnimation("Offset", slide);
    }

    private void RefreshRepositoryActionButtons()
    {
        // Deleting lives in each repository card's menu, which disables it for the last repository.
        Dashboard.CanEditRepository = GetSelectedRepository() is not null && _services.Repositories.SelectedId is not null;
    }

    private void OnWindowSizeChanged(object sender, Microsoft.UI.Xaml.WindowSizeChangedEventArgs args)
    {
        UpdateShellLayout();
    }

    private void UpdateShellLayout()
    {
        double width = Bounds.Width;
        ShellLayoutMode nextMode = width switch
        {
            < 1120 => ShellLayoutMode.Minimal,
            < 1440 => ShellLayoutMode.Compact,
            _ => ShellLayoutMode.Expanded
        };

        if (ShellNavigationView is null || PagesHostGrid is null)
        {
            return;
        }

        UpdateOnlineResponsiveLayout(width);
        ApplyTitleBarSearchVisibility();
        // The Settings and Updates pages lay themselves out when their own size changes.
        UpdateRefinedWorkspaceLayout();

        // The navigation view opens its labelled pane from 1440 px (ExpandedModeThresholdWidth) unless
        // the user collapsed it; below that it shows icons.
        bool expanded = nextMode == ShellLayoutMode.Expanded && !_navigationCollapsed;
        if (_shellLayoutMode == nextMode && _primaryNavExpanded == expanded)
        {
            return;
        }

        _shellLayoutMode = nextMode;
        _primaryNavExpanded = expanded;
        ApplyPageMargins();
        ApplyPrimaryNavigationContent();
    }

    // Page margins from the Windows layout guidance: wider on wide windows, tighter in compact density.
    private void ApplyPageMargins()
    {
        bool wide = _shellLayoutMode == ShellLayoutMode.Expanded;
        bool compact = _interfaceDensity == InterfaceDensity.Compact;
        double side = wide ? (compact ? 24 : 36) : (compact ? 16 : 24);
        double top = wide ? (compact ? 16 : 24) : (compact ? 8 : 16);
        PagesHostGrid.Margin = new Thickness(side, top, side, top);
    }

    private void ApplyPrimaryNavigationContent()
    {
        UpdateRefinedWorkspaceLanguage();
        SetNavigationItem(DashboardNavButton, T("Shell.Dashboard"), T("Shell.Dashboard"));
        SetNavigationItem(RepositoryNavButton, T("Shell.Repositories"), T("Shell.Repositories"));
        SetNavigationItem(OnlineNavButton, T("Shell.Online"), T("Shell.OnlineMods"));
        SetNavigationItem(UpdatesNavButton, T("Shell.Updates"), T("Shell.Updates"));
        SetNavigationItem(ShortcutsNavButton, T("Common.Shortcuts"), T("Common.ModShortcuts"));
        SetNavigationItem(SettingsNavButton, T("Shell.Settings"), T("Shell.Settings"));
        RefreshUpdatesNavBadge();
        NavTasksLabelTextBlock.Text = T("Shell.Downloads");
        string searchName = T("CommandPalette.Placeholder") + " (Ctrl+K)";
        TitleBarSearchBox.PlaceholderText = searchName;
        AutomationProperties.SetName(TitleBarSearchBox, searchName);
        AutomationProperties.SetName(TitleBarSearchButton, searchName);
        ToolTipService.SetToolTip(TitleBarSearchButton, searchName);
        RefreshNavTasks();
    }

    // The label shows in the open pane; the name (sometimes longer) is the tooltip and what Narrator reads.
    private static void SetNavigationItem(NavigationViewItem item, string label, string name)
    {
        item.Content = label;
        ToolTipService.SetToolTip(item, name);
        AutomationProperties.SetName(item, name);
    }

    // Updates carries a badge with the number of Mods that have a new version.
    private void RefreshUpdatesNavBadge()
    {
        if (UpdatesNavInfoBadge is null) return;
        int count = UpdatesView.AvailableUpdateCount;
        UpdatesNavInfoBadge.Value = count;
        UpdatesNavInfoBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // The navigation view shows the current page as its selected item.
    private void RefreshPrimaryNavigationVisuals()
    {
        NavigationViewItem item = _currentPrimarySection switch
        {
            PrimarySection.Repository => RepositoryNavButton,
            PrimarySection.Online => OnlineNavButton,
            PrimarySection.Updates => UpdatesNavButton,
            PrimarySection.Shortcuts => ShortcutsNavButton,
            PrimarySection.Settings => SettingsNavButton,
            _ => DashboardNavButton
        };
        if (!ReferenceEquals(ShellNavigationView.SelectedItem, item)) ShellNavigationView.SelectedItem = item;
    }

    private void OnShellNavigationItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer is not NavigationViewItem { Tag: string tag } item) return;
        var routed = new RoutedEventArgs();
        switch (tag)
        {
            case "Dashboard": OnDashboardNavClicked(item, routed); break;
            case "Repository": OnRepositoryNavClicked(item, routed); break;
            case "Online": OnOnlineNavClicked(item, routed); break;
            case "Updates": OnUpdatesNavClicked(item, routed); break;
            case "Shortcuts": OnShortcutsNavClicked(item, routed); break;
            case "Settings": OnSettingsNavClicked(item, routed); break;
            case "Downloads": FlyoutBase.ShowAttachedFlyout(item); break;
        }
    }

    // Opening the pane in the wide layout is the user's choice and is remembered; the narrower
    // layouts close it themselves, which is not.
    private bool _applyingPaneState;

    private void OnShellNavigationDisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
    {
        if (args.DisplayMode != NavigationViewDisplayMode.Expanded) return;
        _applyingPaneState = true;
        sender.IsPaneOpen = !_navigationCollapsed;
        _applyingPaneState = false;
    }

    private void OnShellNavigationPaneToggled(NavigationView sender, object args)
    {
        if (_applyingPaneState || sender.DisplayMode != NavigationViewDisplayMode.Expanded) return;
        bool collapsed = !sender.IsPaneOpen;
        if (collapsed == _navigationCollapsed) return;
        _navigationCollapsed = collapsed;
        UpdateShellLayout();
        SaveShellConfig();
    }

    // The title bar search box is the entry to the Ctrl+K palette, which has the search itself.
    private void OnTitleBarSearchGotFocus(object sender, RoutedEventArgs e) => OpenCommandPalette();

    private Brush GetAppThemeBrush(string resourceKey)
    {
        // The App*Brush tokens of Themes/Tokens.xaml, in the theme the window shows.
        return ThemeTokens.Get(resourceKey, _services.Appearance.ThemeKey);
    }

    private void OnRepositoryNavClicked(object sender, RoutedEventArgs e)
    {
        SaveSelectedRepositoryFromInputs();
        _currentPrimarySection = PrimarySection.Repository;
        ApplyShellState(refreshRepository: true);
    }

    private void OnOnlineNavClicked(object sender, RoutedEventArgs e)
    {
        SaveSelectedRepositoryFromInputs();
        if (_services.Repositories.SelectedId is null && _services.Repositories.Items.Count > 0)
        {
            _services.Repositories.SelectedId = _services.Repositories.Items[0].Id;
        }

        _currentPrimarySection = PrimarySection.Online;
        ApplyShellState(refreshRepository: false);
        WorkspaceRepository? repository = GetSelectedRepository();
        if (repository is not null)
        {
            _ = EnsureOnlineCharacterCatalogAsync(repository, forceReload: false);
        }
    }

    private void OnUpdatesNavClicked(object sender, RoutedEventArgs e)
    {
        SaveSelectedRepositoryFromInputs();
        _currentPrimarySection = PrimarySection.Updates;
        ApplyShellState(refreshRepository: false);
    }

    private void OnSettingsNavClicked(object sender, RoutedEventArgs e)
    {
        SaveSelectedRepositoryFromInputs();
        _currentPrimarySection = PrimarySection.Settings;
        ApplyShellState(refreshRepository: false);
    }

    private async void OnRootGridLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            TraceStartupStage("RootGrid loaded");
            RootGrid.Loaded -= OnRootGridLoaded;
            InitializeAccessibilityFeatures();
            TraceStartupStage("Accessibility initialized");
            ApplyTheme(_services.Appearance.IsDark);
            ApplyTitleBarSearchVisibility();
            RestoreWindowPlacement();
            UpdateOnlineWorkspaceWidthConstraint();
            DispatcherQueue.TryEnqueue(UpdateOnlineWorkspaceWidthConstraint);
            _services.Images.TrimCache();
            DetectLocalUpdatePackage();
            await RefreshInstallBackupListAsync();

            if (_restoredInterruptedDownloads > 0)
                ShowAppNotification(T("Shell.InterruptedTasksRestored", _restoredInterruptedDownloads));
            await NotifyInterruptedImportsAsync();
            await ChooseFirstInstallGameAsync();
            // Waits for any startup dialog (including the update prompt below) before showing; the
            // getting-started tips come after what's new, so they never sit under a dialog.
            _ = RunUiActionAsync("Show what's new", async () =>
            {
                await ShowWhatsNewOnceAsync();
                await ShowGettingStartedTipsAsync();
            });

            if (_localUpdatePackageVersion is not null && !_hasPromptedForLocalUpdate)
            {
                await PromptAndStartLocalUpdateAsync();
            }
            else
            {
                await CheckForUpdatesIfDueAsync();
            }
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnRootGridLoaded", ex);
        }
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        CloseShortcutPanel();
        CaptureOnlineBrowseSession();
        _browseSaveTimer?.Stop();
        ++_browseRestoreRevision;
        UiCrashDiagnostics.RecordAction("Window closed", width: RootGrid.ActualWidth, height: RootGrid.ActualHeight, closed: true);
        _onlineWindowClosed = true;
        ++_onlineDetailAnimationVersion;
        PersistDownloadTasks();
        _bundleOperationCancellation?.Cancel();
        _existingModImportCancellation?.Cancel();
        _onlineWindowClosed = true;
        InvalidateOnlineDetailRequest();
        _onlineDownloadActions.CancelPreparation();
        foreach (DownloadTaskItem task in _services.Downloads.Tasks.Where(task => DownloadProgressPolicy.IsActive(task.State)))
            if (task.Session?.CanCancel != false) task.Cancellation.Cancel();
        ++_folderRefreshGeneration;
        _folderRefreshCancellation?.Cancel();
        CancelOnlineModListRequest();
        _onlineCharacterAvatarCancellation.Cancel();
        _onlineCharacterAvatarCancellation.Dispose();
        RootGrid.ActualThemeChanged -= OnRootActualThemeChanged;
        SaveWindowPlacement();
        SaveShellConfig();
        DisposeTraySupport();
    }

    private async void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        try
        {
            if (_applyingCombinationProfile || _restoringPersistentRuntime) return;
            bool controlDown = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
            bool altDown = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu).HasFlag(CoreVirtualKeyStates.Down);

            if (e.Key == VirtualKey.Tab
                || IsModifierDown(VirtualKey.LeftWindows) || IsModifierDown(VirtualKey.RightWindows)
                || (altDown && e.Key is VirtualKey.Escape or VirtualKey.F4)) return;

            // Ctrl+K is a keyboard accelerator (InitializeCommandPaletteShortcut); while the
            // palette is open its search box owns the keys.
            if (IsCommandPaletteOpen) return;

            if (controlDown && e.Key == VirtualKey.F)
            {
                (_currentPrimarySection == PrimarySection.Online ? OnlineSearchTextBox : FirstLevelSearchTextBox).Focus(FocusState.Keyboard);
                e.Handled = true;
                return;
            }

            if (controlDown && e.Key == VirtualKey.U)
            {
                NavigateToPrimarySection(PrimarySection.Updates);
                e.Handled = true;
                return;
            }

            if (altDown && e.Key is >= VirtualKey.Number1 and <= VirtualKey.Number5)
            {
                NavigateToPrimarySection((PrimarySection)((int)e.Key - (int)VirtualKey.Number1));
                e.Handled = true;
                return;
            }

            if (e.Key == VirtualKey.Escape && OnlineDetailDrawer.Visibility == Visibility.Visible)
            {
                await CloseOnlineDetailPaneAsync();
                e.Handled = true;
                return;
            }

            if (FocusManager.GetFocusedElement(this.Content.XamlRoot) is TextBox)
            {
                return;
            }

            if (e.Key == VirtualKey.F5)
            {
                if (_currentPrimarySection == PrimarySection.Online)
                {
                    await LoadOnlineModsAsync(forceReload: true);
                }
                else
                {
                    await RefreshListsAsync();
                }
                e.Handled = true;
                return;
            }

            if (e.Key == VirtualKey.Enter)
            {
                var item = GetSelectedSecondLevelItem();
                if (item is not null)
                {
                    await ToggleDirectoryCopyAsync(item);
                    e.Handled = true;
                    return;
                }
            }

            // Mod shortcut rows describe controls used inside the game, not manager
            // navigation/deployment commands. Never run them on Alt+Tab or other keys.
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnRootKeyDown", ex);
        }
    }

    private void OnRootGridPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (ShouldPreservePointerFocus(e.OriginalSource as DependencyObject))
        {
            return;
        }

        ClearFocusedTextInput();
        (GetCurrentShellFocusTarget() ?? RootGrid).Focus(FocusState.Programmatic);
    }

    private static bool ShouldPreservePointerFocus(DependencyObject? source)
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (current is TextBox or Button or ComboBox or ListView or ListViewItem)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private void ClearFocusedTextInput()
    {
        if (_lastFocusedTextBox is null)
        {
            return;
        }

        try
        {
            _lastFocusedTextBox.Select(0, 0);
        }
        catch (Exception)
        {
            // The text box may already be unloaded; clearing its selection is cosmetic.
        }
    }

    private UIElement? GetCurrentShellFocusTarget()
    {
        return _currentPrimarySection switch
        {
            PrimarySection.Dashboard => DashboardNavButton,
            PrimarySection.Repository => RepositoryNavButton,
            PrimarySection.Updates => UpdatesNavButton,
            PrimarySection.Shortcuts => ShortcutsNavButton,
            PrimarySection.Settings => SettingsNavButton,
            _ => OnlineNavButton
        };
    }

    private void ApplyTheme(bool dark)
    {
        _services.Appearance.IsDark = dark;
        RootGrid.RequestedTheme = _systemHighContrast
            ? ElementTheme.Default
            : dark ? ElementTheme.Dark : ElementTheme.Light;
        ApplyWindowChrome(dark);
        // The pages recolour what they drew in code (MainWindow.AppearanceEvents.cs).
        _services.Appearance.NotifyThemeChanged();
        ApplyLanguage();
    }

    // The app header row doubles as the title bar, so the window has one top row instead of two.
    private void InitializeAppTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        try
        {
            if (Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())
            {
                AppWindow.TitleBar.PreferredHeightOption = Microsoft.UI.Windowing.TitleBarHeightOption.Tall;
            }
        }
        catch (Exception ex)
        {
            TraceStartupStage("Title bar height could not be applied: " + ex.Message);
        }
        // The TitleBar control keeps its search box clickable and the rest of the bar draggable.
    }

    private void ApplyWindowChrome(bool dark)
    {
        try
        {
            if (!Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())
            {
                return;
            }

            var titleBar = AppWindow.TitleBar;
            var foreground = dark ? Colors.White : Colors.Black;
            var inactiveForeground = dark
                ? ColorHelper.FromArgb(160, 255, 255, 255)
                : ColorHelper.FromArgb(150, 0, 0, 0);
            var hoverBackground = dark
                ? ColorHelper.FromArgb(28, 255, 255, 255)
                : ColorHelper.FromArgb(20, 0, 0, 0);

            // Content extends under the caption buttons; keep them transparent over the app background.
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonHoverBackgroundColor = hoverBackground;
            titleBar.ButtonHoverForegroundColor = foreground;
            titleBar.ButtonPressedBackgroundColor = hoverBackground;
            titleBar.ButtonForegroundColor = foreground;
            titleBar.ButtonInactiveForegroundColor = inactiveForeground;
        }
        catch
        {
            // Older Windows builds can reject title-bar color customization.
        }
    }

    private void ApplyWindowIcon()
    {
        try
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (File.Exists(iconPath))
            {
                AppWindow.SetIcon(iconPath);
            }
        }
        catch (Exception ex)
        {
            TraceStartupStage("Window icon could not be applied: " + ex.Message);
        }
    }
}
