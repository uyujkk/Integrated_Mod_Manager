using System.Globalization;
using System.Numerics;
using IntegratedModManager.Core.Configuration;
using IntegratedModManager.Core.Layout;
using IntegratedModManager.Core.Online;
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
        if (OnlineDetailsHost is null
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

        // The details drawer floats over the page, so it never takes width from the content.
        double onlineContentWidth = OnlineDetailsHost.ActualWidth > 0
            ? OnlineDetailsHost.ActualWidth
            : windowWidth;

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

        // Narrow windows give the drawer most of the page; full screen caps it so the cards stay visible beside it.
        double drawerWidth = windowWidth < 1220
            ? Math.Clamp(windowWidth - 150, 380, 600)
            : Math.Clamp(windowWidth * 0.34, 480, 760);
        // The drawer opens over the results row, between the toolbar and the paging footer.
        double resultsWidth = OnlineResultsHost.ActualWidth > 0 ? OnlineResultsHost.ActualWidth : onlineContentWidth;
        OnlineDetailDrawer.Width = Math.Min(drawerWidth, Math.Max(320, resultsWidth - 24));
        // The preview keeps a 16:9 frame at the drawer's width instead of a fixed height.
        OnlineDetailHeroBorder.Height = Math.Clamp(Math.Round((OnlineDetailDrawer.Width - 70) * 9 / 16), 220, 400);
        ApplyOnlineDrawerReservation();
    }

    private bool IsOnlineDetailOpen => OnlineDetailDrawer.Visibility == Visibility.Visible;

    // While the drawer is open the cards make room for it: only the results area narrows and
    // re-flows into fewer columns of the same tile size, so no card sits under the drawer.
    // The toolbar, rail and paging keep their full width. If not even one column would fit
    // beside the drawer (narrow windows), it overlays the cards instead.
    private double GetOnlineDrawerReservation()
    {
        if (OnlineDetailDrawer is null || OnlineResultsHost is null || !IsOnlineDetailOpen) return 0;
        double reserved = OnlineDetailDrawer.Width + 12;
        return OnlineResultsHost.ActualWidth - reserved >= OnlineLayoutPolicy.PreferredTileWidth ? reserved : 0;
    }

    private void ApplyOnlineDrawerReservation()
    {
        var margin = new Thickness(0, 0, GetOnlineDrawerReservation(), 0);
        OnlineVirtualizedGridView.Margin = margin;
        OnlineVirtualizedListView.Margin = margin;
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

        ToolTipService.SetToolTip(OnlineListLayoutButton, T("OnlineCards.ListView"));
        ToolTipService.SetToolTip(OnlineGridLayoutButton, T("OnlineCards.GridView"));
        AutomationProperties.SetName(OnlineListLayoutButton, T("OnlineCards.SwitchToListView"));
        AutomationProperties.SetName(OnlineGridLayoutButton, T("OnlineCards.SwitchToGridView"));
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

    // The window's SizeChanged runs before the page is measured again, so its rail width
    // used the old content width after maximizing. Re-run once the page has its new size;
    // this only changes the rail and drawer inside the host, never the host itself.
    private void OnOnlineDetailsHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 0.5) UpdateOnlineResponsiveLayout(Bounds.Width);
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
        return Math.Max(0, availableWidth - GetOnlineDrawerReservation() - 14);
    }

    private void UpdateOnlineGridCardMetrics(double availableWidth)
    {
        if (availableWidth <= 0 || OnlineVirtualizedGridView.ItemsPanelRoot is not ItemsWrapGrid itemsPanel)
        {
            return;
        }

        OnlineGridTileLayout layout = OnlineLayoutPolicy.GridTiles(availableWidth);
        if (Math.Abs(itemsPanel.ItemWidth - layout.ItemWidth) > 0.5) itemsPanel.ItemWidth = layout.ItemWidth;
        if (Math.Abs(itemsPanel.ItemHeight - layout.ItemHeight) > 0.5) itemsPanel.ItemHeight = layout.ItemHeight;
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
                Text = T("OnlineCards.NoPreview"),
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
            Text = $"{GetOnlineCharacterDisplayName(mod.CharacterName)}{(mod.IsCharacterNameInferred ? T("Common.Detected") : string.Empty)} ·",
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
        Border heatMetric = CreateOnlineMetricBadge($"{T("OnlineCards.Heat")} {mod.HotnessScore:F1}", true);
        Border downloadMetric = CreateOnlineMetricBadge($"↓ {mod.Downloads}", false);
        metricsPanel.Children.Add(heatMetric);
        metricsPanel.Children.Add(CreateOnlineMetricBadge($"♥ {mod.Likes}", false));
        metricsPanel.Children.Add(downloadMetric);
        metricsPanel.Children.Add(CreateOnlineMetricBadge($"◉ {mod.Views}", false));
        contentPanel.Children.Add(metricsPanel);
        TrackOnlineCardMetrics(metricsPanel, () =>
        {
            ((TextBlock)heatMetric.Child).Text = $"{T("OnlineCards.Heat")} {mod.HotnessScore:F1}";
            ((TextBlock)downloadMetric.Child).Text = $"↓ {mod.Downloads}";
        });
        var cardStatus = new TextBlock
        {
            Text = $"{T("OnlineCards.Updated")} {mod.UpdatedAt:yyyy-MM-dd}  ·  ID {mod.ItemId}",
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
            Content = T("OnlineCards.Details"),
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
        ToolTipService.SetToolTip(openPageButton, T("OnlineCards.OpenOriginalPage"));
        AutomationProperties.SetName(openPageButton, T("OnlineCards.OpenOriginalPage"));
        openPageButton.Click += async (_, _) => await RunUiActionAsync("OpenExternalUrlAsync", () => OpenExternalUrlAsync(mod.ProfileUrl, T("Common.OpenPageFailed")));
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
            ? T("Common.DownloadAndExtract")
            : T("Common.DownloadAndExtractAgain");
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

    // Grid tiles are an XAML control bound to a view model; the window supplies the actions,
    // the cached preview, the author link and the live download state.
    private UIElement CreateOnlineModTile(OnlineModCard mod)
    {
        var model = new OnlineModTileViewModel(mod,
            openPage: () => RunUiActionAsync("OpenExternalUrlAsync", () => OpenExternalUrlAsync(mod.ProfileUrl, T("Common.OpenPageFailed"))),
            download: () => RunUiActionAsync("DownloadAndExtractOnlineModAsync", () => DownloadAndExtractOnlineModAsync(mod)))
        {
            CharacterLabel = GetOnlineCharacterDisplayName(mod.CharacterName),
            PreviewPlaceholder = T("OnlineCards.NoPreview"),
            HeatLabel = T("OnlineCards.HEAT"),
            OpenPageToolTip = T("OnlineCards.OpenOriginalPage")
        };
        UpdateOnlineTileMetrics(model);
        var tile = new OnlineModTile(model) { ContextFlyout = CreateOnlineCardMenu(mod) };
        if (model.HasPreview) _ = SetCachedOnlineImageAsync(tile.Preview, mod.PreviewUrl!);
        tile.Author.Content = CreateOnlineAuthorLink(mod);
        tile.PointerEntered += (_, _) => AnimateOnlineModCardHover(tile.Card, true);
        tile.PointerExited += (_, _) => AnimateOnlineModCardHover(tile.Card, false);
        TrackOnlineCardMetrics(tile, () => UpdateOnlineTileMetrics(model));
        TrackOnlineTileState(tile, $"{T("OnlineCards.Updated")} {mod.UpdatedAt:yyyy-MM-dd}");
        return tile;
    }

    private void UpdateOnlineTileMetrics(OnlineModTileViewModel model)
    {
        OnlineModCard mod = model.Mod;
        model.HotnessText = mod.HotnessScore.ToString("F1", CultureInfo.CurrentCulture);
        model.HotnessToolTip = $"{T("OnlineCards.Hotness")}: {model.HotnessText}";
        model.LikesText = FormatCompactMetric(mod.Likes);
        model.ViewsText = FormatCompactMetric(mod.Views);
        model.DownloadsText = FormatCompactMetric(mod.Downloads);
        model.StatsToolTip = string.Join(" · ",
            $"{T("OnlineCards.Likes")}: {mod.Likes:N0}",
            $"{T("OnlineCards.Views")}: {mod.Views:N0}",
            $"{T("OnlineCards.Downloads")}: {mod.Downloads:N0}");
    }

    private async void OnOnlineGridItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is OnlineModCard mod)
            await RunUiActionAsync("ShowOnlineModDetailsAsync", () => ShowOnlineModDetailsAsync(mod));
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
            Text = T("Common.Installed"),
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
            T("OnlineCards.InstalledAt", installedPath));
        return indicator;
    }
}
