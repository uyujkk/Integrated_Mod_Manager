using System.Numerics;
using IntegratedModManager.Core.Online;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
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
        AttachTrackedTextInput(SourceTextBox);
        AttachTrackedTextInput(TargetTextBox);
        AttachTrackedTextInput(LauncherTextBox);
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

    private void RefreshSecondaryNavigation()
    {
        SecondaryNavPanel.Children.Clear();

        if (_currentPrimarySection == PrimarySection.Dashboard)
        {
            SecondaryNavPanel.Children.Add(CreateSecondaryNavButton(
                "all-repositories",
                T("Common.AllRepositories"),
                _selectedRepositoryId is null));
        }

        foreach (WorkspaceRepository repository in _repositories)
        {
            SecondaryNavPanel.Children.Add(CreateSecondaryNavButton(
                repository.Id,
                repository.Name,
                repository.Id == _selectedRepositoryId));
        }
    }

    private Button CreateSecondaryNavButton(string key, string label, bool isSelected)
    {
        var button = new Button
        {
            Content = label,
            Tag = key,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            MinHeight = 44,
            Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
            UseSystemFocusVisuals = false
        };
        button.Click += OnSecondaryNavButtonClicked;
        ApplySecondaryNavButtonVisual(button, isSelected);

        return button;
    }

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

    private void ApplyShellState(bool refreshRepository)
    {
        DashboardScrollViewer.Visibility = _currentPrimarySection == PrimarySection.Dashboard ? Visibility.Visible : Visibility.Collapsed;
        if (_workspacePageHost is null)
        {
            WorkspaceScrollViewer.Visibility = _currentPrimarySection == PrimarySection.Repository ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            WorkspaceScrollViewer.Visibility = Visibility.Visible;
            _workspacePageHost.Visibility = _currentPrimarySection == PrimarySection.Repository ? Visibility.Visible : Visibility.Collapsed;
        }
        OnlineScrollViewer.Visibility = _currentPrimarySection == PrimarySection.Online ? Visibility.Visible : Visibility.Collapsed;
        UpdatesScrollViewer.Visibility = _currentPrimarySection == PrimarySection.Updates ? Visibility.Visible : Visibility.Collapsed;
        SettingsScrollViewer.Visibility = _currentPrimarySection == PrimarySection.Settings ? Visibility.Visible : Visibility.Collapsed;
        ShortcutsScrollViewer.Visibility = _currentPrimarySection == PrimarySection.Shortcuts ? Visibility.Visible : Visibility.Collapsed;
        if (_currentPrimarySection == PrimarySection.Shortcuts)
        {
            BuildShortcutPane();
            _ = RefreshShortcutPaneSafelyAsync();
        }

        RefreshPrimaryNavigationVisuals();
        RefreshSecondaryNavigation();
        ApplyLanguage();
        RefreshSettingsPane();
        UpdateShellLayout();

        if (_currentPrimarySection is PrimarySection.Dashboard or PrimarySection.Repository or PrimarySection.Online)
        {
            ApplySelectedRepositoryToInputs();
        }

        if (refreshRepository && _currentPrimarySection == PrimarySection.Repository)
        {
            _ = RefreshListsAsync();
        }

        RefreshDashboard();
        RefreshOnlinePaneV2();
        RefreshUpdatesPane();
        RefreshRepositoryActionButtons();
        SaveShellConfig();
        UIElement activeSection = GetActiveSectionElement();
        RootGrid.DispatcherQueue.TryEnqueue(() => AnimateSectionEntrance(activeSection));
        RootGrid.Focus(FocusState.Programmatic);
    }

    private UIElement GetActiveSectionElement()
    {
        return _currentPrimarySection switch
        {
            PrimarySection.Dashboard => DashboardScrollViewer,
            PrimarySection.Repository => _workspacePageHost is not null ? _workspacePageHost : WorkspaceScrollViewer,
            PrimarySection.Online => OnlineScrollViewer,
            PrimarySection.Updates => UpdatesScrollViewer,
            PrimarySection.Shortcuts => ShortcutsScrollViewer,
            _ => SettingsScrollViewer
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

        visual.Opacity = 0.25f;
        visual.Offset = new Vector3(12f, 0f, 0f);

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.Duration = TimeSpan.FromMilliseconds(170);
        fade.InsertKeyFrame(1f, 1f, easing);

        var slide = compositor.CreateVector3KeyFrameAnimation();
        slide.Duration = TimeSpan.FromMilliseconds(210);
        slide.InsertKeyFrame(1f, Vector3.Zero, easing);

        visual.StartAnimation("Opacity", fade);
        visual.StartAnimation("Offset", slide);
    }

    private void RefreshRepositoryActionButtons()
    {
        // Deleting lives in each repository card's menu, which disables it for the last repository.
        RenameRepositoryButton.IsEnabled = GetSelectedRepository() is not null && _selectedRepositoryId is not null;
    }

    private void OnWindowSizeChanged(object sender, Microsoft.UI.Xaml.WindowSizeChangedEventArgs args)
    {
        UpdateShellLayout();
    }

    private bool _primaryNavShort;

    private void UpdateShellLayout()
    {
        double width = Bounds.Width;
        ShellLayoutMode nextMode = width switch
        {
            < 1120 => ShellLayoutMode.Minimal,
            < 1440 => ShellLayoutMode.Compact,
            _ => ShellLayoutMode.Expanded
        };

        if (PrimaryNavColumn is null
            || SecondaryNavColumn is null
            || SecondaryNavBorder is null
            || PrimaryNavBorder is null
            || DashboardNavButton is null
            || RepositoryNavButton is null
            || OnlineNavButton is null
            || UpdatesNavButton is null
            || SettingsNavButton is null)
        {
            return;
        }

        UpdateOnlineResponsiveLayout(width);
        UpdateUpdatesLayout();
        UpdateRefinedWorkspaceLayout();

        // Wide windows show labelled navigation unless the user collapsed it to the icon rail.
        bool expanded = nextMode == ShellLayoutMode.Expanded && !_navigationCollapsed;
        // On a short window the full-height buttons overran the bottom group, hiding the Downloads
        // icon behind Shortcuts; shorter buttons keep every entry visible.
        bool shortNav = Bounds.Height > 0 && Bounds.Height < 720;
        if (_shellLayoutMode == nextMode && _primaryNavExpanded == expanded && _primaryNavShort == shortNav)
        {
            return;
        }

        _shellLayoutMode = nextMode;
        _primaryNavExpanded = expanded;
        _primaryNavShort = shortNav;
        RefreshNavCollapseButton();
        PrimaryNavColumn.Width = new GridLength(expanded ? 220 : 84);
        SecondaryNavColumn.Width = new GridLength(0);
        SecondaryNavBorder.Visibility = Visibility.Collapsed;
        foreach (Button button in new[] { NavSearchButton, DashboardNavButton, RepositoryNavButton, OnlineNavButton, UpdatesNavButton, ShortcutsNavButton, NavTasksButton, SettingsNavButton })
        {
            button.Width = expanded ? 202 : 66;
            button.Height = expanded ? (shortNav ? 38 : 46) : (shortNav ? 50 : 60);
            button.MinHeight = button.Height;
            button.HorizontalContentAlignment = expanded ? HorizontalAlignment.Left : HorizontalAlignment.Center;
            button.Padding = expanded ? new Thickness(14, 0, 12, 0) : new Thickness(0);
        }
        // The search entry reads as a search box when labelled, and as one more icon in the rail.
        NavSearchButton.Height = NavSearchButton.MinHeight = expanded ? 38 : (shortNav ? 44 : 52);
        NavSearchButton.HorizontalContentAlignment = expanded ? HorizontalAlignment.Stretch : HorizontalAlignment.Center;
        NavSearchButton.Padding = expanded ? new Thickness(12, 0, 10, 0) : new Thickness(0);

        ApplyPrimaryNavigationContent();
    }

    private void ApplyPrimaryNavigationContent()
    {
        UpdateRefinedWorkspaceLanguage();
        DashboardNavButton.Content = CreatePrimaryNavContent("\uE80F", T("Shell.Dashboard"));
        RepositoryNavButton.Content = CreatePrimaryNavContent("\uE8B7", T("Shell.Repositories"));
        OnlineNavButton.Content = CreatePrimaryNavContent("\uE774", T("Shell.Online"));
        UpdatesNavButton.Content = CreatePrimaryNavContent("\uE895", T("Shell.Updates"), UpdatesNavBadge());
        ShortcutsNavButton.Content = CreatePrimaryNavContent("\uE765", T("Common.Shortcuts"));
        ToolTipService.SetToolTip(ShortcutsNavButton, T("Common.ModShortcuts"));
        AutomationProperties.SetName(ShortcutsNavButton, T("Common.ModShortcuts"));
        SettingsNavButton.Content = CreatePrimaryNavContent("\uE713", T("Shell.Settings"));
        NavSearchButton.Content = CreateNavSearchContent();
        string searchName = T("CommandPalette.Placeholder") + " (Ctrl+K)";
        ToolTipService.SetToolTip(NavSearchButton, searchName);
        AutomationProperties.SetName(NavSearchButton, searchName);
        LayoutNavTasksContent();
        RefreshNavTasks();

        ToolTipService.SetToolTip(DashboardNavButton, T("Shell.Dashboard"));
        ToolTipService.SetToolTip(RepositoryNavButton, T("Shell.Repositories"));
        ToolTipService.SetToolTip(OnlineNavButton, T("Shell.OnlineMods"));
        ToolTipService.SetToolTip(UpdatesNavButton, T("Shell.Updates"));
        ToolTipService.SetToolTip(SettingsNavButton, T("Shell.Settings"));

        AutomationProperties.SetName(DashboardNavButton, T("Shell.Dashboard"));
        AutomationProperties.SetName(RepositoryNavButton, T("Shell.Repositories"));
        AutomationProperties.SetName(OnlineNavButton, T("Shell.OnlineMods"));
        AutomationProperties.SetName(UpdatesNavButton, T("Shell.Updates"));
        AutomationProperties.SetName(SettingsNavButton, T("Shell.Settings"));
        RefreshNavCollapseButton();
    }

    // Updates carries a badge with the number of Mods that have a new version.
    private string? UpdatesNavBadge()
    {
        int count = _trackedModUpdateResults.Count(item => item.HasUpdate);
        return count > 0 ? count.ToString(System.Globalization.CultureInfo.CurrentCulture) : null;
    }

    private void RefreshUpdatesNavBadge()
    {
        if (UpdatesNavButton is not null) UpdatesNavButton.Content = CreatePrimaryNavContent("\uE895", T("Shell.Updates"), UpdatesNavBadge());
    }

    private object CreatePrimaryNavContent(string glyph, string label, string? badge = null)
    {
        var icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = 16,
            Opacity = 0.88
        };
        FrameworkElement iconHost = icon;
        if (!string.IsNullOrEmpty(badge))
        {
            var host = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            host.Children.Add(icon);
            host.Children.Add(new Border
            {
                Margin = new Thickness(0, -8, -14, 0), Padding = new Thickness(5, 0, 5, 0), MinWidth = 16, CornerRadius = new CornerRadius(8),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Background = GetAppThemeBrush("AppAccentBrush"),
                Child = new TextBlock
                {
                    Text = badge, FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Colors.White), HorizontalAlignment = HorizontalAlignment.Center
                }
            });
            iconHost = host;
        }
        bool expanded = _primaryNavExpanded;
        var panel = new StackPanel
        {
            Orientation = expanded ? Orientation.Horizontal : Orientation.Vertical,
            Spacing = expanded ? 12 : 6,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = expanded ? HorizontalAlignment.Left : HorizontalAlignment.Center
        };
        panel.Children.Add(iconHost);
        panel.Children.Add(new TextBlock
        {
            Text = label, FontSize = expanded ? 14 : 11,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        return panel;
    }

    private object CreateNavSearchContent()
    {
        if (!_primaryNavExpanded)
        {
            return CreatePrimaryNavContent("", T("Shell.Search"));
        }

        var grid = new Grid { ColumnSpacing = 10, VerticalAlignment = VerticalAlignment.Center };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new FontIcon { Glyph = "", FontSize = 14, Opacity = 0.75 });
        var label = new TextBlock { Text = T("Shell.Search"), FontSize = 13, Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);
        var key = new Border
        {
            Padding = new Thickness(5, 0, 5, 1), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1),
            BorderBrush = GetAppThemeBrush("AppCardBorderBrush"), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = "Ctrl K", FontSize = 11, Opacity = 0.75 }
        };
        Grid.SetColumn(key, 2);
        grid.Children.Add(key);
        return grid;
    }

    // The download entry keeps its named badge and bar (RefreshNavTasks updates them); only their
    // arrangement follows the navigation: a row when labelled, a stack in the icon rail.
    private void LayoutNavTasksContent()
    {
        bool expanded = _primaryNavExpanded;
        NavTasksLabelTextBlock.Text = T("Shell.Downloads");
        NavTasksLabelTextBlock.FontSize = expanded ? 14 : 11;
        NavTasksContent.ColumnDefinitions.Clear();
        NavTasksContent.RowDefinitions.Clear();
        NavTasksContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        NavTasksContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        if (expanded)
        {
            NavTasksContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            NavTasksContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Place(NavTasksIconHost, 0, 0);
            Place(NavTasksLabelTextBlock, 0, 1);
            Place(NavTasksProgressBar, 1, 0, 2);
            NavTasksContent.Width = 170;
            NavTasksProgressBar.Margin = new Thickness(0);
            NavTasksLabelTextBlock.HorizontalAlignment = HorizontalAlignment.Left;
        }
        else
        {
            NavTasksContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Place(NavTasksIconHost, 0, 0);
            Place(NavTasksLabelTextBlock, 1, 0);
            Place(NavTasksProgressBar, 2, 0);
            NavTasksContent.Width = 62;
            NavTasksProgressBar.Margin = new Thickness(10, 0, 10, 0);
            NavTasksLabelTextBlock.HorizontalAlignment = HorizontalAlignment.Center;
        }
    }

    private void OnNavCollapseClicked(object sender, RoutedEventArgs e)
    {
        _navigationCollapsed = !_navigationCollapsed;
        UpdateShellLayout();
        SaveShellConfig();
    }

    // The collapse toggle only matters where the window is wide enough for labelled navigation.
    private void RefreshNavCollapseButton()
    {
        if (NavCollapseButton is null) return;
        NavCollapseButton.Visibility = _shellLayoutMode == ShellLayoutMode.Expanded ? Visibility.Visible : Visibility.Collapsed;
        string label = _navigationCollapsed ? T("Shell.ExpandNavigation") : T("Shell.CollapseNavigation");
        NavCollapseButton.Content = CreatePrimaryNavContent(_navigationCollapsed ? "" : "", label);
        NavCollapseButton.Width = _primaryNavExpanded ? 202 : 66;
        NavCollapseButton.Height = _primaryNavExpanded ? 40 : 52;
        NavCollapseButton.HorizontalContentAlignment = _primaryNavExpanded ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        NavCollapseButton.Padding = _primaryNavExpanded ? new Thickness(14, 0, 12, 0) : new Thickness(0);
        ToolTipService.SetToolTip(NavCollapseButton, label);
        AutomationProperties.SetName(NavCollapseButton, label);
    }

    private void RefreshPrimaryNavigationVisuals()
    {
        ApplyPrimaryButtonVisual(DashboardNavButton, _currentPrimarySection == PrimarySection.Dashboard);
        ApplyPrimaryButtonVisual(RepositoryNavButton, _currentPrimarySection == PrimarySection.Repository);
        ApplyPrimaryButtonVisual(OnlineNavButton, _currentPrimarySection == PrimarySection.Online);
        ApplyPrimaryButtonVisual(UpdatesNavButton, _currentPrimarySection == PrimarySection.Updates);
        ApplyPrimaryButtonVisual(ShortcutsNavButton, _currentPrimarySection == PrimarySection.Shortcuts);
        ApplyPrimaryButtonVisual(SettingsNavButton, _currentPrimarySection == PrimarySection.Settings);
        ApplyPrimaryButtonVisual(NavCollapseButton, false);
        ApplyPrimaryButtonVisual(NavTasksButton, false);
        ApplyPrimaryButtonVisual(NavSearchButton, false);
        // Outlined like a text box, so the search entry is told apart from the pages below it.
        NavSearchButton.BorderBrush = GetAppThemeBrush("AppCardBorderBrush");
    }

    private void ApplyPrimaryButtonVisual(Button button, bool isSelected)
    {
        Brush backgroundBrush = isSelected
            ? GetAppThemeBrush("AppNavSelectedBrush")
            : GetAppThemeBrush("AppNavDefaultBrush");
        Brush pointerOverBrush = isSelected
            ? backgroundBrush
            : GetAppThemeBrush("AppNavHoverBrush");
        Brush borderBrush = isSelected
            ? GetAppThemeBrush("AppNavSelectedBorderBrush")
            : GetAppThemeBrush("AppNavDefaultBrush");
        Brush foregroundBrush = isSelected
            ? GetAppThemeBrush("AppNavSelectedForegroundBrush")
            : GetAppThemeBrush("AppNavDefaultForegroundBrush");
        Brush disabledForegroundBrush = GetAppThemeBrush("AppDisabledForegroundBrush");
        Brush disabledBackgroundBrush = GetAppThemeBrush("AppInsetBackgroundBrush");
        Brush disabledBorderBrush = GetAppThemeBrush("AppCardBorderBrush");

        button.Background = backgroundBrush;
        button.BorderBrush = borderBrush;
        button.Foreground = foregroundBrush;

        button.Resources["ButtonBackground"] = backgroundBrush;
        button.Resources["ButtonBackgroundPointerOver"] = pointerOverBrush;
        button.Resources["ButtonBackgroundPressed"] = backgroundBrush;
        button.Resources["ButtonBackgroundDisabled"] = disabledBackgroundBrush;

        button.Resources["ButtonBorderBrush"] = borderBrush;
        button.Resources["ButtonBorderBrushPointerOver"] = pointerOverBrush;
        button.Resources["ButtonBorderBrushPressed"] = borderBrush;
        button.Resources["ButtonBorderBrushDisabled"] = disabledBorderBrush;

        button.Resources["ButtonForeground"] = foregroundBrush;
        button.Resources["ButtonForegroundPointerOver"] = foregroundBrush;
        button.Resources["ButtonForegroundPressed"] = foregroundBrush;
        button.Resources["ButtonForegroundDisabled"] = disabledForegroundBrush;
    }

    private Brush GetAppThemeBrush(string resourceKey)
    {
        string themeKey = _isDarkTheme ? "Dark" : "Light";
        ResourceDictionary themeDictionary = (ResourceDictionary)Application.Current.Resources.ThemeDictionaries[themeKey];
        return (Brush)themeDictionary[resourceKey];
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
        if (_selectedRepositoryId is null && _repositories.Count > 0)
        {
            _selectedRepositoryId = _repositories[0].Id;
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

    private void OnSecondaryNavButtonClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key })
        {
            return;
        }

        SaveSelectedRepositoryFromInputs();
        _selectedRepositoryId = key == "all-repositories" ? null : key;
        ResetOnlineRepositoryContext();
        ApplyShellState(refreshRepository: _currentPrimarySection == PrimarySection.Repository);
    }

    private async void OnRootGridLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            TraceStartupStage("RootGrid loaded");
            RootGrid.Loaded -= OnRootGridLoaded;
            InitializeAccessibilityFeatures();
            TraceStartupStage("Accessibility initialized");
            ApplyTheme(_isDarkTheme);
            RestoreWindowPlacement();
            UpdateOnlineWorkspaceWidthConstraint();
            DispatcherQueue.TryEnqueue(UpdateOnlineWorkspaceWidthConstraint);
            TrimOnlineImageCache();
            DetectLocalUpdatePackage();
            await RefreshInstallBackupListAsync();

            if (_restoredInterruptedDownloads > 0)
                ShowAppNotification($"已恢复 {_restoredInterruptedDownloads} 个中断任务，请到更新页检查后手动重试。", $"Restored {_restoredInterruptedDownloads} interrupted tasks. Review them in Updates and retry manually.");
            await NotifyInterruptedImportsAsync();
            // Waits for any startup dialog (including the update prompt below) before showing.
            _ = RunUiActionAsync("Show what's new", ShowWhatsNewOnceAsync);

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
        foreach (DownloadTaskItem task in _downloadTasks.Where(task => DownloadProgressPolicy.IsActive(task.State)))
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
        if (_currentPrimarySection == PrimarySection.Repository && _selectedRepositoryId is not null)
        {
            return SecondaryNavPanel.Children.OfType<Button>().FirstOrDefault(button => string.Equals(button.Tag as string, _selectedRepositoryId, StringComparison.Ordinal));
        }

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
        _isDarkTheme = dark;
        RootGrid.RequestedTheme = _systemHighContrast
            ? ElementTheme.Default
            : dark ? ElementTheme.Dark : ElementTheme.Light;
        ApplyWindowChrome(dark);
        RenderModSummary();
        RefreshPrimaryNavigationVisuals();
        RefreshSecondaryNavigation();
        UpdateShortcutKeyFocusVisuals();
        // Binding cards take their key-cap colours when created; drop them so they are rebuilt.
        _mainShortcutControls.Clear();
        _mainBindings = [];
        RenderShortcutButtons();
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
        // The title bar holds no controls: search and downloads live in the side navigation.
    }

    private void OnNavSearchClicked(object sender, RoutedEventArgs e) => OpenCommandPalette();

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
