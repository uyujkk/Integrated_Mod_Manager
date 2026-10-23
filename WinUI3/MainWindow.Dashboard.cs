using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private void RefreshDashboard()
    {
        if (DashboardRepoCountTextBlock is null
            || DashboardRepositoriesPanel is null
            || DashboardModCountTextBlock is null
            || DashboardReadyCountTextBlock is null
            || DashboardRepositoryComboBox is null)
        {
            return;
        }

        if (_selectedRepositoryId is null && _repositories.Count > 0)
        {
            _selectedRepositoryId = _repositories[0].Id;
        }

        PopulateDashboardRepositorySwitcher();
        DashboardRepoCountTextBlock.Text = _repositories.Count.ToString();

        int totalMods = 0;
        int readyRepositories = 0;
        DashboardRepositoriesPanel.Children.Clear();

        foreach (WorkspaceRepository repository in _repositories)
        {
            RepositorySnapshot snapshot = BuildRepositorySnapshot(repository);
            totalMods += snapshot.ModCount;
            if (snapshot.IsReady)
            {
                readyRepositories++;
            }

            DashboardRepositoriesPanel.Children.Add(CreateDashboardCard(repository, snapshot));
        }

        DashboardModCountTextBlock.Text = totalMods.ToString();
        DashboardReadyCountTextBlock.Text = readyRepositories.ToString();

        RefreshRepositoryPresetStatus(BuiltInRepositoryPresets[0], DashboardZzzPresetStatusTextBlock);
        RefreshRepositoryPresetStatus(BuiltInRepositoryPresets[1], DashboardGenshinPresetStatusTextBlock);
        RefreshRepositoryPresetStatus(BuiltInRepositoryPresets[2], DashboardHsrPresetStatusTextBlock);
        RefreshRepositoryPresetStatus(BuiltInRepositoryPresets[3], DashboardEndfieldPresetStatusTextBlock);
        DashboardOpenRepositoryButton.IsEnabled = GetSelectedRepository() is not null;
        RefreshRefinedDashboardPathHeader();
        RefreshTrayStatus();
    }

    private void PopulateDashboardRepositorySwitcher()
    {
        _isApplyingDashboardRepositorySelection = true;
        try
        {
            DashboardRepositoryComboBox.Items.Clear();
            foreach (WorkspaceRepository repository in _repositories)
            {
                DashboardRepositoryComboBox.Items.Add(new ComboBoxItem
                {
                    Content = repository.Name,
                    Tag = repository.Id
                });
            }

            DashboardRepositoryComboBox.SelectedItem = DashboardRepositoryComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag as string, _selectedRepositoryId, StringComparison.Ordinal));
        }
        finally
        {
            _isApplyingDashboardRepositorySelection = false;
        }
    }

    private void RefreshRepositoryPresetStatus(RepositoryPresetInfo preset, TextBlock statusTextBlock)
    {
        WorkspaceRepository? existing = FindRepositoryForPreset(preset);
        statusTextBlock.Text = existing is null
            ? T("Dashboard.CreateItAndConfigureLocalPaths")
            : T("Dashboard.AlreadyAdded", existing.Name);
    }

    private WorkspaceRepository? FindRepositoryForPreset(RepositoryPresetInfo preset)
    {
        return _repositories.FirstOrDefault(repository =>
            string.Equals(repository.OnlineSourceSite, DefaultOnlineSourceSite, StringComparison.OrdinalIgnoreCase)
            && IsRepositoryForPreset(repository, preset));
    }

    private static bool IsRepositoryForPreset(WorkspaceRepository repository, RepositoryPresetInfo preset)
    {
        string categoryId = repository.OnlineCategoryId?.Trim() ?? string.Empty;
        string gameName = repository.OnlineGameName?.Trim() ?? string.Empty;
        return string.Equals(categoryId, preset.CategoryId, StringComparison.OrdinalIgnoreCase)
            || (string.Equals(preset.Id, "genshin", StringComparison.OrdinalIgnoreCase)
                && string.Equals(categoryId, "17510", StringComparison.OrdinalIgnoreCase))
            || string.Equals(gameName, preset.DisplayNameEn, StringComparison.OrdinalIgnoreCase)
            || string.Equals(gameName, preset.DisplayNameZh, StringComparison.OrdinalIgnoreCase);
    }

    private RepositorySnapshot BuildRepositorySnapshot(WorkspaceRepository repository)
    {
        bool sourceReady;
        bool targetReady;

        try
        {
            sourceReady = !string.IsNullOrWhiteSpace(repository.SourcePath) && Directory.Exists(repository.SourcePath);
        }
        catch (Exception)
        {
            sourceReady = false;
        }

        try
        {
            targetReady = !string.IsNullOrWhiteSpace(repository.TargetPath) && Directory.Exists(repository.TargetPath);
        }
        catch (Exception)
        {
            targetReady = false;
        }

        if (!sourceReady)
        {
            return new RepositorySnapshot(0, 0, targetReady);
        }

        try
        {
            string[] firstLevelDirectories = Directory.GetDirectories(repository.SourcePath);
            int modCount = 0;
            foreach (string firstLevelDirectory in firstLevelDirectories)
            {
                try
                {
                    modCount += Directory.GetDirectories(firstLevelDirectory).Length;
                }
                catch (Exception)
                {
                    // Skip unreadable folders so one bad directory does not crash the dashboard.
                }
            }

            return new RepositorySnapshot(firstLevelDirectories.Length, modCount, sourceReady && targetReady);
        }
        catch (Exception)
        {
            return new RepositorySnapshot(0, 0, targetReady);
        }
    }

    private UIElement CreateDashboardCard(WorkspaceRepository repository, RepositorySnapshot snapshot)
    {
        bool isSelected = string.Equals(repository.Id, _selectedRepositoryId, StringComparison.Ordinal);
        var border = new Border
        {
            Style = (Style)Application.Current.Resources["InsetBorderStyle"],
            Padding = new Thickness(16),
            BorderThickness = isSelected ? new Thickness(2) : new Thickness(1),
            BorderBrush = GetAppThemeBrush(isSelected ? "AppNavSelectedBorderBrush" : "AppCardBorderBrush")
        };

        var stackPanel = new StackPanel { Spacing = 10 };
        var titleGrid = new Grid { ColumnSpacing = 12 };
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleGrid.Children.Add(new TextBlock
        {
            Text = repository.Name,
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });

        var stateBadge = new Border
        {
            Style = (Style)Application.Current.Resources["SoftBadgeBorderStyle"],
            Background = GetAppThemeBrush(isSelected ? "AppSecondarySelectedBrush" : "AppInsetBackgroundBrush"),
            BorderBrush = GetAppThemeBrush(isSelected ? "AppNavSelectedBorderBrush" : "AppCardBorderBrush"),
            Child = new TextBlock
            {
                Text = isSelected ? T("Dashboard.Current") : (snapshot.IsReady ? T("Dashboard.Ready") : T("Dashboard.SetupNeeded")),
                Style = (Style)Application.Current.Resources["CaptionTextStyle"],
                Foreground = GetAppThemeBrush(isSelected ? "AppSecondarySelectedForegroundBrush" : "AppSecondaryDefaultForegroundBrush")
            }
        };
        Grid.SetColumn(stateBadge, 1);
        titleGrid.Children.Add(stateBadge);
        stackPanel.Children.Add(titleGrid);

        stackPanel.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(repository.OnlineGameName)
                ? $"GameBanana · {repository.OnlineCategoryId}"
                : $"{repository.OnlineGameName} · GameBanana {repository.OnlineCategoryId}",
            Style = (Style)Application.Current.Resources["CaptionTextStyle"],
            TextWrapping = TextWrapping.Wrap
        });
        stackPanel.Children.Add(new TextBlock
        {
            Text = $"{T("Dashboard.ModStorage")}: {TrimPreviewPath(repository.SourcePath)}",
            Opacity = 0.74,
            TextWrapping = TextWrapping.Wrap
        });
        stackPanel.Children.Add(new TextBlock
        {
            Text = $"{T("Dashboard.Target")}: {TrimPreviewPath(repository.TargetPath)}",
            Opacity = 0.74,
            TextWrapping = TextWrapping.Wrap
        });
        stackPanel.Children.Add(new TextBlock
        {
            Text = $"{T("Common.Launcher")}: {TrimPreviewPath(repository.LauncherPath)}",
            Opacity = 0.74,
            TextWrapping = TextWrapping.Wrap
        });
        stackPanel.Children.Add(new TextBlock
        {
            Text = repository.UseDirectoryLinks
                ? T("Dashboard.DeploymentDirectoryJunction")
                : T("Dashboard.DeploymentCopy"),
            Opacity = 0.74
        });
        stackPanel.Children.Add(new TextBlock
        {
            Text = $"{T("Common.Categories")} {snapshot.FirstLevelCount}  ·  {T("Dashboard.Mods")} {snapshot.ModCount}",
            FontWeight = Microsoft.UI.Text.FontWeights.Medium
        });

        var actionGrid = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        actionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var switchButton = new Button
        {
            Content = isSelected ? T("Dashboard.InUse") : T("Dashboard.SwitchToThisRepository"),
            Style = (Style)Application.Current.Resources[isSelected ? "PrimaryButtonStyle" : "SecondaryButtonStyle"],
            IsEnabled = !isSelected,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        switchButton.Click += (_, _) => SwitchRepository(repository.Id, openManager: false);
        actionGrid.Children.Add(switchButton);

        var manageButton = new Button
        {
            Content = T("Dashboard.Manage"),
            Style = (Style)Application.Current.Resources["SecondaryButtonStyle"]
        };
        manageButton.Click += (_, _) => SwitchRepository(repository.Id, openManager: true);
        Grid.SetColumn(manageButton, 1);
        actionGrid.Children.Add(manageButton);

        var wikiButton = new Button
        {
            Width = 42,
            Height = 38,
            Padding = new Thickness(0),
            Content = new FontIcon { Glyph = "\uE774", FontSize = 15 },
            Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
            IsEnabled = Uri.TryCreate(repository.WikiUrl, UriKind.Absolute, out _)
        };
        ToolTipService.SetToolTip(wikiButton, T("Dashboard.OpenGameWiki"));
        wikiButton.Click += (_, _) => OpenRepositoryWiki(repository);
        Grid.SetColumn(wikiButton, 2);
        actionGrid.Children.Add(wikiButton);
        stackPanel.Children.Add(actionGrid);

        border.Child = stackPanel;
        ToolTipService.SetToolTip(border, T("Dashboard.DoubleClickToSwitchToThis"));
        border.DoubleTapped += (_, args) =>
        {
            if (IsSourceInsideButton(args.OriginalSource as DependencyObject))
            {
                return;
            }

            args.Handled = true;
            SwitchRepository(repository.Id, openManager: false);
        };
        return border;
    }

    private static string TrimPreviewPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "-";
        }

        return path.Length > 48 ? "..." + path[^45..] : path;
    }

    private void OnDashboardNavClicked(object sender, RoutedEventArgs e)
    {
        SaveSelectedRepositoryFromInputs();
        _currentPrimarySection = PrimarySection.Dashboard;
        ApplyShellState(refreshRepository: false);
    }

    private void OnDashboardRepositorySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingDashboardRepositorySelection
            || DashboardRepositoryComboBox.SelectedItem is not ComboBoxItem { Tag: string repositoryId })
        {
            return;
        }

        SwitchRepository(repositoryId, openManager: false);
    }

    private void OnDashboardOpenRepositoryClicked(object sender, RoutedEventArgs e)
    {
        if (_selectedRepositoryId is not null)
        {
            SwitchRepository(_selectedRepositoryId, openManager: true);
        }
    }
}
