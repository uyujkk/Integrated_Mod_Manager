using System.Globalization;
using System.Numerics;
using IntegratedModManager.Core.Configuration;
using IntegratedModManager.Core.Layout;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private void UpdateOnlineResponsiveLayout(double windowWidth)
    {
        TraceOnlineUiState("Responsive layout");
        if (OnlineDetailsSplitView is null
            || OnlineWorkspaceGrid is null
            || OnlineCharacterRailColumn is null
            || OnlineResultsColumn is null
            || OnlineCharacterRailRow is null
            || OnlineResultsRow is null
            || OnlineCharacterRailHost is null
            || OnlineResultsHost is null
            || OnlineCharacterAvatarPanel is null
            || OnlineCharacterRailScrollViewer is null
            || OnlineCharacterRailHintTextBlock is null)
        {
            return;
        }

        double onlineContentWidth = OnlineDetailsSplitView.ActualWidth > 0
            ? OnlineDetailsSplitView.ActualWidth
            : windowWidth;
        if (OnlineDetailsSplitView.IsPaneOpen
            && OnlineDetailsSplitView.DisplayMode == SplitViewDisplayMode.Inline)
        {
            onlineContentWidth -= OnlineDetailsSplitView.OpenPaneLength;
        }

        OnlineCharacterRailLayout railLayout = OnlineLayoutPolicy.CharacterRail(onlineContentWidth, RootGrid.ActualHeight);
        bool useHorizontalCharacterRail = railLayout.Horizontal;
        bool characterRailModeChanged = _useHorizontalOnlineCharacterRail != useHorizontalCharacterRail;
        _useHorizontalOnlineCharacterRail = useHorizontalCharacterRail;
        if (useHorizontalCharacterRail)
        {
            OnlineCharacterRailColumn.Width = new GridLength(1, GridUnitType.Star);
            OnlineResultsColumn.Width = new GridLength(0);
            OnlineCharacterRailRow.Height = GridLength.Auto;
            OnlineResultsRow.Height = new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(OnlineCharacterRailHost, 0);
            Grid.SetRow(OnlineCharacterRailHost, 0);
            Grid.SetColumn(OnlineResultsHost, 0);
            Grid.SetRow(OnlineResultsHost, 1);
            OnlineCharacterRailHost.MaxHeight = 148;
            OnlineCharacterAvatarPanel.Orientation = Orientation.Horizontal;
            OnlineCharacterAvatarPanel.HorizontalAlignment = HorizontalAlignment.Left;
            OnlineCharacterAvatarPanel.Margin = new Thickness(0, 0, 0, 12);
            OnlineCharacterRailScrollViewer.HorizontalScrollMode = ScrollMode.Enabled;
            OnlineCharacterRailScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            OnlineCharacterRailScrollViewer.VerticalScrollMode = ScrollMode.Disabled;
            OnlineCharacterRailScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            OnlineCharacterRailHintTextBlock.Visibility = Visibility.Collapsed;
        }
        else
        {
            OnlineCharacterRailColumn.Width = new GridLength(railLayout.ColumnWidth);
            OnlineResultsColumn.Width = new GridLength(1, GridUnitType.Star);
            OnlineCharacterRailRow.Height = new GridLength(1, GridUnitType.Star);
            OnlineResultsRow.Height = new GridLength(0);
            Grid.SetColumn(OnlineCharacterRailHost, 0);
            Grid.SetRow(OnlineCharacterRailHost, 0);
            Grid.SetColumn(OnlineResultsHost, 1);
            Grid.SetRow(OnlineResultsHost, 0);
            OnlineCharacterRailHost.ClearValue(FrameworkElement.MaxHeightProperty);
            OnlineCharacterAvatarPanel.Orientation = Orientation.Vertical;
            OnlineCharacterAvatarPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
            OnlineCharacterAvatarPanel.Margin = new Thickness(0, 0, 4, 0);
            OnlineCharacterRailScrollViewer.HorizontalScrollMode = ScrollMode.Disabled;
            OnlineCharacterRailScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            OnlineCharacterRailScrollViewer.VerticalScrollMode = ScrollMode.Enabled;
            OnlineCharacterRailScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            OnlineCharacterRailHintTextBlock.Visibility = Visibility.Collapsed;
        }

        if (characterRailModeChanged)
        {
            _onlineCharacterStripSignature = null;
            RefreshOnlineCharacterAvatarStrip();
        }

        if (windowWidth < 1220)
        {
            OnlineDetailsSplitView.DisplayMode = SplitViewDisplayMode.Overlay;
            OnlineDetailsSplitView.OpenPaneLength = Math.Clamp(windowWidth - 150, 380, 600);
            DispatcherQueue.TryEnqueue(UpdateOnlineWorkspaceWidthConstraint);
            return;
        }

        OnlineDetailsSplitView.DisplayMode = SplitViewDisplayMode.Inline;
        OnlineDetailsSplitView.OpenPaneLength = Math.Clamp(windowWidth * 0.34, 460, 580);
        DispatcherQueue.TryEnqueue(UpdateOnlineWorkspaceWidthConstraint);
    }

    private UIElement CreateOnlinePreviewCard(string title, string body, string status)
    {
        Border border = new()
        {
            Style = (Style)Application.Current.Resources["InsetBorderStyle"],
            Padding = new Thickness(14)
        };

        StackPanel stackPanel = new() { Spacing = 6 };
        stackPanel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        stackPanel.Children.Add(new TextBlock
        {
            Text = body,
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["MutedTextStyle"]
        });
        stackPanel.Children.Add(new TextBlock
        {
            Text = status,
            Style = (Style)Application.Current.Resources["CaptionTextStyle"]
        });

        border.Child = stackPanel;
        return border;
    }

    private async Task AnimateOnlineListExitAsync()
    {
        FrameworkElement resultsElement = GetOnlineResultsElement();
        var visual = ElementCompositionPreview.GetElementVisual(resultsElement);
        visual.StopAnimation("Opacity");

        if (_reduceMotion || _onlineVisibleMods.Count == 0)
        {
            visual.Opacity = 1f;
            return;
        }

        var compositor = visual.Compositor;
        var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.4f, 0f), new Vector2(1f, 1f));
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.Duration = TimeSpan.FromMilliseconds(110);
        fade.InsertKeyFrame(1f, 0f, easing);
        visual.StartAnimation("Opacity", fade);
        await Task.Delay(135);
        visual.StopAnimation("Opacity");
        visual.Opacity = 1f;
    }

    private void AnimateOnlineCardsEntrance()
    {
        if (!_animateOnlineCardsOnNextRefresh)
        {
            return;
        }

        _animateOnlineCardsOnNextRefresh = false;
        FrameworkElement resultsElement = GetOnlineResultsElement();
        var visual = ElementCompositionPreview.GetElementVisual(resultsElement);
        visual.StopAnimation("Opacity");
        if (ShouldReduceAnimations)
        {
            visual.Opacity = 1f;
            return;
        }

        var compositor = visual.Compositor;
        var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1f), new Vector2(0.3f, 1f));
        visual.Opacity = 0f;

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.Duration = TimeSpan.FromMilliseconds(180);
        fade.InsertKeyFrame(1f, 1f, easing);
        visual.StartAnimation("Opacity", fade);
    }

    private void OnOnlineSortSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_restoringBrowseControls) return;
        if (OnlineSortComboBox.SelectedItem is ComboBoxItem { Tag: OnlineSortMode sortMode })
        {
            if (_onlineSortMode == sortMode) return;
            ResetOnlineBrowsePosition();
            _onlineSortMode = sortMode;
            RefreshOnlinePaneV2();
            SaveShellConfig();
        }
    }

    private void OnOnlineListLayoutClicked(object sender, RoutedEventArgs e)
    {
        SetOnlineCardLayout(OnlineCardLayoutMode.List);
    }

    private void OnOnlineGridLayoutClicked(object sender, RoutedEventArgs e)
    {
        SetOnlineCardLayout(OnlineCardLayoutMode.Grid);
    }

    private void SetOnlineCardLayout(OnlineCardLayoutMode layoutMode)
    {
        if (_onlineCardLayoutMode == layoutMode)
        {
            return;
        }

        CaptureOnlineBrowseSession();
        _pendingBrowsePosition = _activeBrowseSessionKey is not null && _onlinePreferences.BrowseSessions.TryGetValue(_activeBrowseSessionKey, out var savedPosition) ? savedPosition : new();
        _onlineCardLayoutMode = layoutMode;
        _animateOnlineCardsOnNextRefresh = true;
        SaveShellConfig();
        RefreshOnlinePaneV2();
    }

    private FrameworkElement GetOnlineResultsElement()
    {
        return _onlineCardLayoutMode == OnlineCardLayoutMode.Grid
            ? OnlineVirtualizedGridView
            : OnlineVirtualizedListView;
    }

    private void UpdateOnlineCardLayoutControls()
    {
        bool showGrid = _onlineCardLayoutMode == OnlineCardLayoutMode.Grid;
        OnlineVirtualizedListView.Visibility = showGrid ? Visibility.Collapsed : Visibility.Visible;
        OnlineVirtualizedGridView.Visibility = showGrid ? Visibility.Visible : Visibility.Collapsed;
        ApplySecondaryNavButtonVisual(OnlineListLayoutButton, !showGrid);
        ApplySecondaryNavButtonVisual(OnlineGridLayoutButton, showGrid);

        ToolTipService.SetToolTip(OnlineListLayoutButton, L("列表显示", "List view"));
        ToolTipService.SetToolTip(OnlineGridLayoutButton, L("方块显示", "Grid view"));
        AutomationProperties.SetName(OnlineListLayoutButton, L("切换为列表显示", "Switch to list view"));
        AutomationProperties.SetName(OnlineGridLayoutButton, L("切换为方块显示", "Switch to grid view"));
    }

    private void OnOnlineListCardPresenterDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (sender is ContentControl presenter)
        {
            presenter.Content = args.NewValue is OnlineModCard mod
                ? CreateOnlineModCardV2(mod)
                : null;
        }
    }

    private void OnOnlineGridCardPresenterDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (sender is ContentControl presenter)
        {
            presenter.Content = args.NewValue is OnlineModCard mod
                ? CreateOnlineModTile(mod)
                : null;
        }
    }

    private void OnOnlineResultsHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        TraceOnlineUiState("Results size changed");
        if (_onlineCardLayoutMode == OnlineCardLayoutMode.Grid)
        {
            UpdateOnlineGridCardMetrics(GetOnlineGridAvailableWidth(e.NewSize.Width));
        }
    }

    private void OnOnlineWorkspaceGridSizeChanged(object sender, SizeChangedEventArgs e)
    {
        TraceOnlineUiState("Workspace size changed");
        UpdateOnlineWorkspaceWidthConstraint();
    }

    private void UpdateOnlineWorkspaceWidthConstraint()
    {
        if (OnlineResultsHost is null
            || OnlineWorkspaceGrid is null
            || OnlineVirtualizedGridView is null)
        {
            return;
        }

        // SplitView already measures its content after reserving the inline details pane.
        // Explicit widths created a feedback loop because the next calculation used the
        // centered position of the previously narrowed workspace.
        OnlineWorkspaceGrid.ClearValue(FrameworkElement.WidthProperty);
        OnlineResultsHost.ClearValue(FrameworkElement.WidthProperty);

        if (_onlineCardLayoutMode == OnlineCardLayoutMode.Grid)
        {
            double availableWidth = OnlineVirtualizedGridView.ActualWidth > 0
                ? OnlineVirtualizedGridView.ActualWidth
                : OnlineResultsHost.ActualWidth;
            UpdateOnlineGridCardMetrics(GetOnlineGridAvailableWidth(availableWidth));
        }
    }

    private double GetOnlineGridAvailableWidth(double fallbackWidth)
    {
        double resultsWidth = OnlineResultsHost?.ActualWidth ?? 0;
        double availableWidth = resultsWidth > 0 ? resultsWidth : fallbackWidth;
        return Math.Max(0, availableWidth - 14);
    }

    private void UpdateOnlineGridCardMetrics(double availableWidth)
    {
        if (availableWidth < 520)
        {
            return;
        }

        const double preferredCellWidth = 290;
        const double itemHeight = 360;
        int columns = Math.Clamp((int)Math.Floor(availableWidth / preferredCellWidth), 2, 7);
        double itemWidth = Math.Floor(availableWidth / columns);
        if (OnlineVirtualizedGridView.ItemsPanelRoot is not ItemsWrapGrid itemsPanel)
        {
            return;
        }

        if (Math.Abs(itemsPanel.ItemWidth - itemWidth) > 0.5)
        {
            itemsPanel.ItemWidth = itemWidth;
        }
        itemsPanel.ItemHeight = itemHeight;
    }

    private static bool IsSourceInsideButton(DependencyObject? source)
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (current is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private UIElement CreateOnlineModCardV2(OnlineModCard mod)
    {
        TrackedModOrigin? installedOrigin = FindInstalledOnlineMod(mod);
        Border border = new()
        {
            ContextFlyout = CreateOnlineCardMenu(mod),
            Style = (Style)Application.Current.Resources["InsetBorderStyle"],
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(12)
        };
        border.Tapped += async (_, args) =>
        {
            try
            {
                if (IsSourceInsideButton(args.OriginalSource as DependencyObject))
                {
                    return;
                }

                await ShowOnlineModDetailsAsync(mod);
            }
            catch (Exception ex)
            {
                ReportUiActionFailure("border.Tapped", ex);
            }
        };
        border.PointerEntered += (_, _) => AnimateOnlineModCardHover(border, true);
        border.PointerExited += (_, _) => AnimateOnlineModCardHover(border, false);

        Grid rootGrid = new() { ColumnSpacing = 12, Padding = new Thickness(0, 10, 10, 10) };
        rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(132) });
        rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var accentBar = new Border
        {
            Width = 4,
            Margin = new Thickness(0, 2, 0, 2),
            CornerRadius = new CornerRadius(2),
            Background = GetAppThemeBrush("AppNavSelectedBorderBrush")
        };
        Grid.SetColumn(accentBar, 0);
        rootGrid.Children.Add(accentBar);

        Border imageHost = new()
        {
            Style = (Style)Application.Current.Resources["InsetBorderStyle"],
            MinHeight = 88,
            Height = 88,
            CornerRadius = new CornerRadius(10)
        };

        if (!string.IsNullOrWhiteSpace(mod.PreviewUrl))
        {
            var previewImage = new Image
            {
                Stretch = Stretch.UniformToFill
            };
            imageHost.Child = previewImage;
            _ = SetCachedOnlineImageAsync(previewImage, mod.PreviewUrl);
        }
        else
        {
            imageHost.Child = new TextBlock
            {
                Text = L("暂无缩略图", "No preview"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Style = (Style)Application.Current.Resources["CaptionTextStyle"]
            };
        }

        Grid.SetColumn(imageHost, 1);
        rootGrid.Children.Add(imageHost);

        StackPanel contentPanel = new() { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        contentPanel.Children.Add(new TextBlock
        {
            Text = mod.Title,
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        var authorRow = new Grid { ColumnSpacing = 6 };
        authorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        authorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        authorRow.Children.Add(new TextBlock
        {
            Text = $"{GetOnlineCharacterDisplayName(mod.CharacterName)}{(mod.IsCharacterNameInferred ? L("（自动识别）", " (detected)") : string.Empty)} ·",
            Style = (Style)Application.Current.Resources["MutedTextStyle"],
            VerticalAlignment = VerticalAlignment.Center
        });
        HyperlinkButton authorLink = CreateOnlineAuthorLink(mod);
        Grid.SetColumn(authorLink, 1);
        authorRow.Children.Add(authorLink);
        contentPanel.Children.Add(authorRow);
        // Reserve badge space so finishing an installation does not resize the row.
        var installedHost = new ContentControl { Height = 24, HorizontalContentAlignment = HorizontalAlignment.Left };
        contentPanel.Children.Add(installedHost);
        var metricsPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        Border heatMetric = CreateOnlineMetricBadge($"{L("热度", "Heat")} {mod.HotnessScore:F1}", true);
        Border downloadMetric = CreateOnlineMetricBadge($"↓ {mod.Downloads}", false);
        metricsPanel.Children.Add(heatMetric);
        metricsPanel.Children.Add(CreateOnlineMetricBadge($"♥ {mod.Likes}", false));
        metricsPanel.Children.Add(downloadMetric);
        metricsPanel.Children.Add(CreateOnlineMetricBadge($"◉ {mod.Views}", false));
        contentPanel.Children.Add(metricsPanel);
        TrackOnlineCardMetrics(metricsPanel, () =>
        {
            ((TextBlock)heatMetric.Child).Text = $"{L("热度", "Heat")} {mod.HotnessScore:F1}";
            ((TextBlock)downloadMetric.Child).Text = $"↓ {mod.Downloads}";
        });
        var cardStatus = new TextBlock
        {
            Text = $"{L("更新", "Updated")} {mod.UpdatedAt:yyyy-MM-dd}  ·  ID {mod.ItemId}",
            Style = (Style)Application.Current.Resources["CaptionTextStyle"],
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        contentPanel.Children.Add(cardStatus);

        StackPanel actionsPanel = new()
        {
            Width = 124,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };
        Button detailsButton = new()
        {
            Content = L("查看详情", "Details"),
            Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
            MinHeight = 36,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        detailsButton.Click += async (_, _) => await RunUiActionAsync("ShowOnlineModDetailsAsync", () => ShowOnlineModDetailsAsync(mod));
        actionsPanel.Children.Add(detailsButton);

        Grid quickActions = new()
        {
            ColumnSpacing = 6,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        quickActions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        quickActions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Button openPageButton = new()
        {
            Content = new FontIcon { Glyph = "\uE774", FontSize = 15 },
            Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
            MinHeight = 34,
            MinWidth = 0,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        ToolTipService.SetToolTip(openPageButton, L("打开原页面", "Open original page"));
        AutomationProperties.SetName(openPageButton, L("打开原页面", "Open original page"));
        openPageButton.Click += async (_, _) => await RunUiActionAsync("OpenExternalUrlAsync", () => OpenExternalUrlAsync(mod.ProfileUrl, L("打开页面失败", "Open page failed")));
        quickActions.Children.Add(openPageButton);

        Button downloadButton = new()
        {
            Content = new FontIcon { Glyph = "\uE896", FontSize = 15 },
            Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
            MinHeight = 34,
            MinWidth = 0,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            IsEnabled = !string.IsNullOrWhiteSpace(mod.DownloadUrl) && !IsOnlineDownloadActionBlocked(mod)
        };
        Grid.SetColumn(downloadButton, 1);
        string downloadActionText = installedOrigin is null
            ? L("下载并解压", "Download and extract")
            : L("重新下载并解压", "Download and extract again");
        ToolTipService.SetToolTip(downloadButton, downloadActionText);
        AutomationProperties.SetName(downloadButton, downloadActionText);
        downloadButton.Click += async (_, _) => await RunUiActionAsync("RunOnlineCardDownloadAsync", () => RunOnlineCardDownloadAsync(mod, downloadButton));
        quickActions.Children.Add(downloadButton);
        actionsPanel.Children.Add(quickActions);
        TrackOnlineCardState(mod, downloadButton, installedHost, cardStatus, cardStatus.Text);

        Grid.SetColumn(contentPanel, 2);
        rootGrid.Children.Add(contentPanel);

        Grid.SetColumn(actionsPanel, 3);
        rootGrid.Children.Add(actionsPanel);

        border.Child = rootGrid;
        return border;
    }

    private UIElement CreateOnlineModTile(OnlineModCard mod)
    {
        TrackedModOrigin? installedOrigin = FindInstalledOnlineMod(mod);
        Border border = new()
        {
            ContextFlyout = CreateOnlineCardMenu(mod),
            Style = (Style)Application.Current.Resources["InsetBorderStyle"],
            Tag = "OnlineModTile",
            Height = 350,
            Margin = new Thickness(5),
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(16),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        border.Tapped += async (_, args) =>
        {
            try
            {
                if (!IsSourceInsideButton(args.OriginalSource as DependencyObject))
                {
                    await ShowOnlineModDetailsAsync(mod);
                }
            }
            catch (Exception ex)
            {
                ReportUiActionFailure("border.Tapped", ex);
            }
        };
        border.PointerEntered += (_, _) => AnimateOnlineModCardHover(border, true);
        border.PointerExited += (_, _) => AnimateOnlineModCardHover(border, false);

        Grid root = new();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(142) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid imageLayer = new();
        Border imageHost = new()
        {
            Background = GetAppThemeBrush("AppPreviewBackgroundBrush"),
            CornerRadius = new CornerRadius(15, 15, 0, 0)
        };
        if (!string.IsNullOrWhiteSpace(mod.PreviewUrl))
        {
            var image = new Image { Stretch = Stretch.UniformToFill };
            imageHost.Child = image;
            _ = SetCachedOnlineImageAsync(image, mod.PreviewUrl);
        }
        else
        {
            imageHost.Child = new TextBlock
            {
                Text = L("暂无缩略图", "No preview"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Style = (Style)Application.Current.Resources["CaptionTextStyle"]
            };
        }
        imageLayer.Children.Add(imageHost);

        string characterLabel = GetOnlineCharacterDisplayName(mod.CharacterName);
        Grid characterContent = new();
        characterContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
        characterContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        characterContent.Children.Add(new Border
        {
            Width = 3,
            Background = GetAppThemeBrush("AppNavSelectedBorderBrush")
        });
        var characterText = new TextBlock
        {
            Text = characterLabel,
            Margin = new Thickness(9, 0, 10, 0),
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White),
            VerticalAlignment = VerticalAlignment.Center,
            MaxLines = 1,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(characterText, 1);
        characterContent.Children.Add(characterText);

        var characterBanner = new Border
        {
            Height = 34,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = new SolidColorBrush(ColorHelper.FromArgb(190, 12, 17, 25)),
            Child = characterContent
        };
        ToolTipService.SetToolTip(characterBanner, characterLabel);
        imageLayer.Children.Add(characterBanner);

        Border heatBadge = CreateOnlineHeatIndicator(mod.HotnessScore);
        heatBadge.Margin = new Thickness(10);
        heatBadge.HorizontalAlignment = HorizontalAlignment.Right;
        heatBadge.VerticalAlignment = VerticalAlignment.Top;
        imageLayer.Children.Add(heatBadge);
        var installedHost = new ContentControl
        {
            Margin = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        imageLayer.Children.Add(installedHost);
        root.Children.Add(imageLayer);

        StackPanel content = new() { Margin = new Thickness(12, 10, 12, 7), Spacing = 4 };
        content.Children.Add(new TextBlock
        {
            Text = mod.Title,
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(CreateOnlineAuthorLink(mod));
        var cardStatus = new TextBlock
        {
            Text = $"{L("更新", "Updated")} {mod.UpdatedAt:yyyy-MM-dd}",
            Style = (Style)Application.Current.Resources["CaptionTextStyle"],
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        content.Children.Add(cardStatus);

        Grid metrics = new() { ColumnSpacing = 6, Margin = new Thickness(0, 2, 0, 0) };
        metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Border likes = CreateOnlineTileStat("♥", FormatCompactMetric(mod.Likes), L("点赞", "Likes"));
        Border views = CreateOnlineTileStat("◉", FormatCompactMetric(mod.Views), L("查看", "Views"));
        Border downloads = CreateOnlineTileStat("↓", FormatCompactMetric(mod.Downloads), L("下载", "Downloads"));
        metrics.Children.Add(likes);
        Grid.SetColumn(views, 1);
        metrics.Children.Add(views);
        Grid.SetColumn(downloads, 2);
        metrics.Children.Add(downloads);
        content.Children.Add(metrics);
        TrackOnlineCardMetrics(metrics, () =>
        {
            // Both indicators are icon/label + value StackPanels; the value is the last TextBlock.
            ((TextBlock)((StackPanel)heatBadge.Child).Children[^1]).Text = mod.HotnessScore.ToString("F1", CultureInfo.CurrentCulture);
            ((TextBlock)((StackPanel)downloads.Child).Children[^1]).Text = FormatCompactMetric(mod.Downloads);
        });
        Grid.SetRow(content, 1);
        root.Children.Add(content);

        Grid actions = new() { Margin = new Thickness(12, 0, 12, 12), ColumnSpacing = 7 };
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });

        Button detailsButton = new()
        {
            Content = L("查看详情", "Details"),
            MinHeight = 36,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Style = (Style)Application.Current.Resources["PrimaryButtonStyle"]
        };
        detailsButton.Click += async (_, _) => await RunUiActionAsync("ShowOnlineModDetailsAsync", () => ShowOnlineModDetailsAsync(mod));
        actions.Children.Add(detailsButton);

        Button openPageButton = new()
        {
            Content = new FontIcon { Glyph = "\uE774", FontSize = 15 },
            MinWidth = 38,
            MinHeight = 36,
            Padding = new Thickness(0),
            Style = (Style)Application.Current.Resources["SecondaryButtonStyle"]
        };
        Grid.SetColumn(openPageButton, 1);
        ToolTipService.SetToolTip(openPageButton, L("打开原页面", "Open original page"));
        openPageButton.Click += async (_, _) => await RunUiActionAsync("OpenExternalUrlAsync", () => OpenExternalUrlAsync(mod.ProfileUrl, L("打开页面失败", "Open page failed")));
        actions.Children.Add(openPageButton);

        Button downloadButton = new()
        {
            Content = new FontIcon { Glyph = "\uE896", FontSize = 15 },
            MinWidth = 38,
            MinHeight = 36,
            Padding = new Thickness(0),
            IsEnabled = !string.IsNullOrWhiteSpace(mod.DownloadUrl) && !IsOnlineDownloadActionBlocked(mod),
            Style = (Style)Application.Current.Resources["SecondaryButtonStyle"]
        };
        Grid.SetColumn(downloadButton, 2);
        ToolTipService.SetToolTip(
            downloadButton,
            installedOrigin is null
                ? L("下载并解压", "Download and extract")
                : L("重新下载并解压", "Download and extract again"));
        downloadButton.Click += async (_, _) => await RunUiActionAsync("RunOnlineCardDownloadAsync", () => RunOnlineCardDownloadAsync(mod, downloadButton));
        actions.Children.Add(downloadButton);

        Grid.SetRow(actions, 2);
        TrackOnlineCardState(mod, downloadButton, installedHost, cardStatus, cardStatus.Text);
        root.Children.Add(actions);
        border.Child = root;
        return border;
    }

    private Border CreateOnlineTileStat(string symbol, string value, string label)
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 5
        };
        content.Children.Add(new TextBlock
        {
            Text = symbol,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        });
        content.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            MaxLines = 1,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        });

        var stat = new Border
        {
            MinHeight = 30,
            Padding = new Thickness(7, 4, 7, 4),
            Background = GetAppThemeBrush("AppSecondaryDefaultBrush"),
            BorderBrush = GetAppThemeBrush("AppCardBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = content
        };
        ToolTipService.SetToolTip(stat, $"{label}: {value}");
        return stat;
    }

    private string FormatCompactMetric(int value)
    {
        if (_currentLanguage == AppLanguage.ZhCn)
        {
            return value >= 100_000_000
                ? $"{value / 100_000_000d:0.#}亿"
                : value >= 10_000
                    ? $"{value / 10_000d:0.#}万"
                    : value.ToString("N0", CultureInfo.CurrentCulture);
        }

        return value >= 1_000_000
            ? $"{value / 1_000_000d:0.#}M"
            : value >= 1_000
                ? $"{value / 1_000d:0.#}K"
                : value.ToString("N0", CultureInfo.CurrentCulture);
    }

    private void AnimateOnlineModCardHover(Border card, bool isPointerOver)
    {
        card.Background = GetAppThemeBrush(isPointerOver ? "AppSecondarySelectedBrush" : "AppInsetBackgroundBrush");
        if (ShouldReduceAnimations)
        {
            return;
        }

        var visual = ElementCompositionPreview.GetElementVisual(card);
        visual.StopAnimation("Scale");
        visual.CenterPoint = new Vector3((float)(card.ActualWidth / 2d), (float)(card.ActualHeight / 2d), 0f);
        var compositor = visual.Compositor;
        var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1f), new Vector2(0.3f, 1f));
        var scale = compositor.CreateVector3KeyFrameAnimation();
        scale.Duration = TimeSpan.FromMilliseconds(isPointerOver ? 150 : 120);
        float value = isPointerOver ? 1.006f : 1f;
        scale.InsertKeyFrame(1f, new Vector3(value, value, 1f), easing);
        visual.StartAnimation("Scale", scale);
    }

    private Border CreateOnlineMetricBadge(string text, bool highlighted)
    {
        return new Border
        {
            Background = GetAppThemeBrush(highlighted ? "AppAccentSoftBrush" : "AppSecondaryDefaultBrush"),
            BorderBrush = GetAppThemeBrush(highlighted ? "AppNavSelectedBorderBrush" : "AppCardBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4, 8, 4),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 12,
                MaxLines = 1,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontWeight = highlighted ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal
            }
        };
    }

    private Border CreateOnlineInstalledIndicator(string installedPath)
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(new FontIcon
        {
            Glyph = "\uE73E",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        });
        content.Children.Add(new TextBlock
        {
            Text = L("已安装", "Installed"),
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            MaxLines = 1,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        });

        var indicator = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = GetAppThemeBrush("AppAccentSoftBrush"),
            BorderBrush = GetAppThemeBrush("AppNavSelectedBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4, 8, 4),
            Child = content
        };
        ToolTipService.SetToolTip(
            indicator,
            L($"已安装到：{installedPath}", $"Installed at: {installedPath}"));
        return indicator;
    }

    private Border CreateOnlineHeatIndicator(double hotnessScore)
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(new TextBlock
        {
            Text = L("热度", "HEAT"),
            FontSize = 10,
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(210, 255, 255, 255)),
            VerticalAlignment = VerticalAlignment.Center
        });
        content.Children.Add(new TextBlock
        {
            Text = hotnessScore.ToString("F1", CultureInfo.CurrentCulture),
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White),
            VerticalAlignment = VerticalAlignment.Center
        });

        var indicator = new Border
        {
            Background = new SolidColorBrush(ColorHelper.FromArgb(210, 24, 37, 55)),
            BorderBrush = GetAppThemeBrush("AppNavSelectedBorderBrush"),
            BorderThickness = new Thickness(0, 0, 0, 2),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(8, 4, 8, 4),
            Child = content
        };
        ToolTipService.SetToolTip(indicator, $"{L("热度", "Hotness")}: {hotnessScore:F1}");
        return indicator;
    }
}
