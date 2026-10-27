using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using IntegratedModManager.Core.Configuration;
using IntegratedModManager.Core.Online;
using IntegratedModManager.Core.Updates;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private static UpdateCheckInterval ParseUpdateCheckInterval(string? value)
    {
        return Enum.TryParse(value, ignoreCase: true, out UpdateCheckInterval interval)
            ? interval
            : UpdateCheckInterval.Manual;
    }

    private TimeSpan? GetUpdateCheckIntervalTimeSpan()
    {
        return _updateCheckInterval switch
        {
            UpdateCheckInterval.Daily => TimeSpan.FromDays(1),
            UpdateCheckInterval.EveryThreeDays => TimeSpan.FromDays(3),
            UpdateCheckInterval.Weekly => TimeSpan.FromDays(7),
            _ => null
        };
    }

    private bool ShouldAutoCheckForUpdates()
    {
        TimeSpan? interval = GetUpdateCheckIntervalTimeSpan();
        if (interval is null)
        {
            return false;
        }

        return !_lastUpdateCheckUtc.HasValue || DateTimeOffset.UtcNow - _lastUpdateCheckUtc.Value >= interval.Value;
    }

    private async Task CheckForUpdatesIfDueAsync()
    {
        bool startupCheckDue = AppSelfUpdateEnabled && _checkAppUpdatesOnStartup
            && (ShouldAutoCheckForUpdates()
                || _updateCheckInterval == UpdateCheckInterval.Manual
                    && (!_lastUpdateCheckUtc.HasValue || DateTimeOffset.UtcNow - _lastUpdateCheckUtc.Value >= TimeSpan.FromMinutes(30)));
        if (startupCheckDue)
        {
            await CheckForUpdatesAsync(showDialogs: false, promptIfAvailable: true);
        }

        if (ShouldAutoCheckForModUpdates())
        {
            await CheckTrackedModUpdatesAsync(showDialogs: false);
        }
    }

    private TimeSpan? GetModUpdateCheckIntervalTimeSpan()
    {
        return _modUpdateCheckInterval switch
        {
            UpdateCheckInterval.Daily => TimeSpan.FromDays(1),
            UpdateCheckInterval.EveryThreeDays => TimeSpan.FromDays(3),
            UpdateCheckInterval.Weekly => TimeSpan.FromDays(7),
            _ => null
        };
    }

    private bool ShouldAutoCheckForModUpdates()
    {
        TimeSpan? interval = GetModUpdateCheckIntervalTimeSpan();
        if (interval is null)
        {
            return false;
        }

        return !_lastModUpdateCheckUtc.HasValue || DateTimeOffset.UtcNow - _lastModUpdateCheckUtc.Value >= interval.Value;
    }

    private void SetUpdateStatus(string zh, string en)
    {
        _updateStatusZh = zh;
        _updateStatusEn = en;
        UpdateStatusTextBlock.Text = L(zh, en);
    }

    private void SetModUpdateStatus(string zh, string en)
    {
        _modUpdateStatusZh = zh;
        _modUpdateStatusEn = en;
        if (ModUpdateStatusTextBlock is not null)
        {
            ModUpdateStatusTextBlock.Text = L(zh, en);
        }
    }

    private void PopulateUpdateCheckIntervalOptions()
    {
        _isApplyingUpdateCheckIntervalSelection = true;
        try
        {
            PopulateStableIntervalOptions(UpdateCheckIntervalComboBox, _updateCheckInterval);
        }
        finally
        {
            _isApplyingUpdateCheckIntervalSelection = false;
        }
    }

    private void RefreshUpdateDetailsView()
    {
        if (!AppSelfUpdateEnabled)
        {
            LatestReleaseTitleTextBlock.Text = T("Updates.StandaloneBeta", AppVersion);
            LatestReleaseMetaTextBlock.Text = T("Updates.ManualDownloadExtractSeparatelyFromThe");
            UpdateNotesTextBlock.Text = T("Updates.ModUpdateTestBuildFixesInterval");
            return;
        }
        string latestTag = string.IsNullOrWhiteSpace(_latestReleaseTag) ? T("Updates.NotCheckedYet") : _latestReleaseTag!;
        string latestTitle = string.IsNullOrWhiteSpace(_latestReleaseTitle)
            ? T("Updates.LatestReleaseDetailsWillAppearHere")
            : NormalizeUiText(_latestReleaseTitle);

        string publishedText = TryParseDateTimeOffset(_latestReleasePublishedAt) is DateTimeOffset publishedAt
            ? publishedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : T("Updates.UnknownTime");

        string lastCheckedText = _lastUpdateCheckUtc.HasValue
            ? _lastUpdateCheckUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : T("Updates.NotCheckedYet");

        LatestReleaseTitleTextBlock.Text = T("Updates.LatestRelease", latestTag);
        LatestReleaseMetaTextBlock.Text = T("Updates.TitlePublishedLastChecked", latestTitle, publishedText, lastCheckedText);
        UpdateNotesTextBlock.Text = string.IsNullOrWhiteSpace(_latestReleaseBody)
            ? T("Updates.GitHubReleaseNotesWillAppearHere")
            : NormalizeUiText(_latestReleaseBody);
        // Before the first check there is nothing but placeholders, so the box stays one line.
        Visibility details = string.IsNullOrWhiteSpace(_latestReleaseTag) ? Visibility.Collapsed : Visibility.Visible;
        LatestReleaseMetaTextBlock.Visibility = details;
        UpdateNotesScrollViewer.Visibility = details;
    }

    private void PopulateModUpdateIntervalOptions()
    {
        _isApplyingModUpdateIntervalSelection = true;
        try
        {
            PopulateStableIntervalOptions(ModUpdateIntervalComboBox, _modUpdateCheckInterval);
        }
        finally
        {
            _isApplyingModUpdateIntervalSelection = false;
        }
    }

    private void RefreshUpdatesPane()
    {
        RefreshUpdatesNavBadge();
        UpdatesPageHeader.Title = T("Updates.ModUpdates");
        UpdatesPageHeader.Description = T("Updates.CheckInstalledModsForNewVersions");
        ConfigurationProfilesTitleTextBlock.Text = T("Updates.ModCombinationPresets");
        ConfigurationProfilesHintTextBlock.Text = T("Updates.SaveEnabledModsAndPersistentState");
        CaptureCombinationStateCheckBox.Content = T("Updates.IncludeD3dxUserIniParametersWhen");
        BrowseCombinationUserIniButton.Content = T("Common.Browse");
        CreateConfigurationProfileButton.Content = T("Updates.NewProfile");
        UpdateConfigurationProfileButton.Content = T("Updates.UpdateProfile");
        ApplyConfigurationProfileButton.Content = T("Updates.PreviewAndRestore");
        DeleteConfigurationProfileButton.Content = new FontIcon { Glyph = "\uE74D", FontSize = 16 };
        ToolTipService.SetToolTip(DeleteConfigurationProfileButton, T("Updates.DeleteTheSelectedProfile"));
        InstallSafetyTitleTextBlock.Text = T("Updates.InstallSafety");
        InstallSafetyHintTextBlock.Text = T("Updates.CheckFileConflictsBeforeInstallationCopies");
        ConflictDetectionToggleSwitch.Header = T("Updates.DetectConflictsBeforeInstall");
        ConflictDetectionToggleSwitch.OnContent = T("Common.On");
        ConflictDetectionToggleSwitch.OffContent = T("Common.Off");
        ConflictDetectionToggleSwitch.IsOn = _enableConflictDetection;
        RollbackLastInstallButton.Content = T("Updates.UndoLastModChange");
        DownloadTaskCenterTitleTextBlock.Text = T("Updates.DownloadTasks");
        DownloadTaskCenterHintTextBlock.Text = T("Updates.ShowsOnlineModDownloadAndExtraction");
        ClearDownloadTasksButton.Content = T("Updates.ClearFinished");
        RefreshBackupManagerText();
        _ = RefreshInstallBackupListAsync();
        RefreshConfigurationProfiles();
        RefreshDownloadTaskCenter();
        TrackedModSettingsTitleTextBlock.Text =T("Updates.CheckSettings");
        TrackedModSettingsHintTextBlock.Text = T("Updates.CheckOnStartupAtTheSelected");
        ModUpdateIntervalLabelTextBlock.Text = T("Updates.CheckInterval");
        ModUpdateIntervalHintTextBlock.Text = T("Updates.OnlyOnlineInstalledModsWithA");
        ToolTipService.SetToolTip(ModUpdateIntervalComboBox, TrackedModSettingsHintTextBlock.Text + "\n" + ModUpdateIntervalHintTextBlock.Text);
        CheckModUpdatesButton.Content = _isCheckingModUpdates ? T("Common.Checking") : T("Updates.CheckNow");
        TrackedModsTitleTextBlock.Text = T("Updates.TrackedMods");

        if (string.IsNullOrWhiteSpace(_modUpdateStatusZh) || string.IsNullOrWhiteSpace(_modUpdateStatusEn))
        {
            SetModUpdateStatus("尚未检查已追踪 Mod 更新。", "Tracked mod updates have not been checked yet.");
        }
        else
        {
            ModUpdateStatusTextBlock.Text = L(_modUpdateStatusZh, _modUpdateStatusEn);
        }

        string lastCheckedText = _lastModUpdateCheckUtc.HasValue
            ? _lastModUpdateCheckUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : T("Updates.NotCheckedYet");
        int trackedCount = _trackedModOrigins.Count;
        int updateCount = _trackedModUpdateResults.Count(item => item.HasUpdate);
        TrackedModsSummaryTextBlock.Text = T("Updates.TrackedModsLastCheckedUpdatesAvailable", trackedCount, lastCheckedText, updateCount);

        PopulateModUpdateIntervalOptions();
        RefreshTrackedModsList();
    }

    // One row of the tracked Mods list: the check result, or the bare origin before any check.
    private sealed record TrackedModEntry(TrackedModUpdateResult Result, bool IsChecked, TrackedModStatus Status);

    private TrackedModFilter _trackedModFilter;
    private ToggleButton[] _trackedModChips = [];

    // Filters, search and order come from UpdatesListPolicy; the repeater only builds visible rows.
    private void RefreshTrackedModsList()
    {
        if (TrackedModsRepeater is null) return;
        TrackedModEntry[] entries = _trackedModUpdateResults.Count > 0
            ? [.. _trackedModUpdateResults.Select(result => new TrackedModEntry(result, true,
                UpdatesListPolicy.Status(true, result.CheckFailed, result.HasUpdate)))]
            : [.. _trackedModOrigins.Values.Select(origin => new TrackedModEntry(new TrackedModUpdateResult
            {
                Path = origin.Path,
                Title = origin.Title,
                ProfileUrl = origin.ProfileUrl,
                PreviewUrl = origin.PreviewUrl,
                ItemId = origin.ItemId,
                LastKnownUpdatedAt = origin.LastKnownUpdatedAt,
                LatestUpdatedAt = origin.LastKnownUpdatedAt,
                StatusTextZh = string.Empty,
                StatusTextEn = string.Empty
            }, false, TrackedModStatus.NotChecked))];

        if (_trackedModChips.Length == 0)
            _trackedModChips = CreateFilterChips(TrackedModsFilterPanel, _trackedModFilter, filter =>
            {
                _trackedModFilter = filter;
                RefreshTrackedModsList();
            });
        foreach (ToggleButton chip in _trackedModChips)
        {
            var filter = (TrackedModFilter)chip.Tag;
            int count = entries.Count(entry => UpdatesListPolicy.Matches(entry.Status, filter));
            chip.Content = filter switch
            {
                TrackedModFilter.Updates => T("Updates.FilterUpdates", count),
                TrackedModFilter.Failed => T("Updates.FilterFailed", count),
                TrackedModFilter.UpToDate => T("Updates.FilterUpToDate", count),
                _ => T("Updates.FilterAll", count)
            };
            chip.IsChecked = filter == _trackedModFilter;
        }
        TrackedModsSearchBox.PlaceholderText = T("Updates.SearchTrackedMods");
        TrackedModsFilterPanel.Visibility = TrackedModsSearchBox.Visibility = entries.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        List<TrackedModEntry> visible = UpdatesListPolicy.Order(
                entries.Where(entry => UpdatesListPolicy.Matches(entry.Status, _trackedModFilter)
                    && UpdatesListPolicy.MatchesSearch(entry.Result.Title, entry.Result.Path, TrackedModsSearchBox.Text)),
                entry => entry.Status, entry => entry.Result.Title)
            .ToList();
        if (TrackedModsSearchBox.Parent is Grid toolbar && toolbar.Tag is null)
        {
            toolbar.Tag = true;
            toolbar.SizeChanged += (_, _) => LayoutTrackedModsToolbar();
        }
        LayoutTrackedModsToolbar();
        TrackedModsRepeater.ItemTemplate ??= new DelegateElementFactory(item => CreateTrackedModRow((TrackedModEntry)item));
        TrackedModsRepeater.ItemsSource = visible;

        if (entries.Length == 0)
        {
            // Mods are tracked once they come from the online page, so the empty state leads there.
            var empty = new StackPanel { Spacing = 10 };
            empty.Children.Add(CreateInfoCard(T("Updates.NoTrackedOnlineModsYet"), T("Updates.AfterDownloadingAndExtractingFromThe")));
            var browse = new Button { Content = T("Dashboard.BrowseOnline"), Style = (Style)Application.Current.Resources["PrimaryButtonStyle"] };
            browse.Click += OnOnlineNavClicked;
            empty.Children.Add(browse);
            TrackedModsEmptyHost.Content = empty;
        }
        else
        {
            TrackedModsEmptyHost.Content = visible.Count == 0
                ? new EmptyState { Glyph = "\uE721", Title = T("Updates.NoMatchingTrackedMods") }
                : null;
        }
    }

    private void OnTrackedModsSearchChanged(object sender, TextChangedEventArgs e) => RefreshTrackedModsList();

    // The search box sits beside the chips while they fit, and drops to its own row when they don't.
    private void LayoutTrackedModsToolbar()
    {
        if (TrackedModsSearchBox.Parent is not Grid toolbar || toolbar.ActualWidth <= 0) return;
        TrackedModsFilterPanel.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        bool besideChips = toolbar.ActualWidth >= TrackedModsFilterPanel.DesiredSize.Width + 12 + 220;
        if (toolbar.RowDefinitions.Count == 0)
        {
            toolbar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            toolbar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            toolbar.RowSpacing = 8;
        }
        toolbar.ColumnDefinitions[1].Width = besideChips ? new GridLength(220) : new GridLength(0);
        toolbar.ColumnSpacing = besideChips ? 12 : 0;
        Grid.SetRow(TrackedModsSearchBox, besideChips ? 0 : 1);
        Grid.SetColumn(TrackedModsSearchBox, besideChips ? 1 : 0);
        toolbar.RowDefinitions[1].Height = besideChips ? new GridLength(0) : GridLength.Auto;
    }

    // A row of single-choice chips; the caller keeps the choice and rebuilds its list.
    private ToggleButton[] CreateFilterChips<TFilter>(Panel host, TFilter selected, Action<TFilter> choose) where TFilter : struct, Enum
    {
        host.Children.Clear();
        return Enum.GetValues<TFilter>().Select(filter =>
        {
            var chip = new ToggleButton
            {
                MinHeight = 28, Padding = new Thickness(10, 2, 10, 2), CornerRadius = new CornerRadius(14),
                Tag = filter, IsChecked = EqualityComparer<TFilter>.Default.Equals(filter, selected)
            };
            foreach (string state in new[] { "Checked", "CheckedPointerOver", "CheckedPressed" })
            {
                chip.Resources["ToggleButtonBackground" + state] = GetAppThemeBrush("AppSecondarySelectedBrush");
                chip.Resources["ToggleButtonForeground" + state] = GetAppThemeBrush("AppSecondarySelectedForegroundBrush");
                chip.Resources["ToggleButtonBorderBrush" + state] = GetAppThemeBrush("AppNavSelectedBorderBrush");
            }
            chip.Click += (_, _) => choose(filter);
            host.Children.Add(chip);
            return chip;
        }).ToArray();
    }

    // Compact row: thumbnail, title with recorded and latest dates, a status pill and the actions.
    private UIElement CreateTrackedModRow(TrackedModEntry entry)
    {
        TrackedModUpdateResult result = entry.Result;
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var thumb = new Border
        {
            Width = 48, Height = 48, CornerRadius = new CornerRadius(6),
            Background = GetAppThemeBrush("AppPreviewBackgroundBrush")
        };
        if (!string.IsNullOrWhiteSpace(result.PreviewUrl))
        {
            var image = new Image { Stretch = Stretch.UniformToFill };
            thumb.Child = image;
            _ = SetCachedOnlineImageAsync(image, result.PreviewUrl);
        }
        else thumb.Child = new FontIcon { Glyph = "\uE8B9", FontSize = 16, Opacity = 0.6 };
        grid.Children.Add(thumb);

        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock
        {
            Text = result.Title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis
        };
        ToolTipService.SetToolTip(title, $"{result.Title}\n{T("Updates.LocalPath")}: {result.Path}");
        text.Children.Add(title);
        text.Children.Add(new TextBlock
        {
            Text = $"{T("Updates.Recorded")}: {FormatDateForDisplay(result.LastKnownUpdatedAt)}  ·  {T("Updates.Latest")}: {FormatDateForDisplay(result.LatestUpdatedAt)}",
            Style = (Style)Application.Current.Resources["CaptionTextStyle"], MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis
        });
        // Only a failed check needs its message on the row; other results are said by the pill.
        if (entry.Status == TrackedModStatus.Failed && !string.IsNullOrWhiteSpace(result.StatusTextZh))
        {
            var message = new TextBlock
            {
                Text = L(result.StatusTextZh, result.StatusTextEn), FontSize = 12, Foreground = GetAppThemeBrush("AppDangerBrush"),
                MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis
            };
            ToolTipService.SetToolTip(message, message.Text);
            text.Children.Add(message);
        }
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        (string Label, string Background, string Foreground) pill = entry.Status switch
        {
            TrackedModStatus.Update => (T("Updates.Update"), "AppAccentSoftBrush", "AppAccentBrush"),
            TrackedModStatus.Failed => (T("Updates.CheckFailed"), "AppDangerSoftBrush", "AppDangerBrush"),
            TrackedModStatus.UpToDate => (T("Updates.UpToDate"), "AppSuccessSoftBrush", "AppSuccessBrush"),
            _ => (T("Updates.NotChecked"), "AppSecondaryDefaultBrush", "AppSecondaryDefaultForegroundBrush")
        };
        var badge = new Border
        {
            CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 2, 8, 2), VerticalAlignment = VerticalAlignment.Center,
            Background = GetAppThemeBrush(pill.Background),
            Child = new TextBlock { Text = pill.Label, FontSize = 12, Foreground = GetAppThemeBrush(pill.Foreground) }
        };
        Grid.SetColumn(badge, 2);
        grid.Children.Add(badge);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        if (result.HasUpdate)
        {
            var replaceButton = new Button
            {
                Content = T("Common.DownloadAndReplace"), Style = (Style)Application.Current.Resources["PrimaryButtonStyle"], MinHeight = 32,
                IsEnabled = !_modUpdatesInProgress.Contains(result.Path) && Directory.Exists(result.Path)
            };
            replaceButton.Click += async (_, _) => await RunUiActionAsync("Download and replace Mod", () => DownloadAndReplaceTrackedModAsync(result));
            actions.Children.Add(replaceButton);
        }
        var openPageButton = new Button
        {
            Content = new FontIcon { Glyph = "\uE774", FontSize = 14 }, Width = 32, Height = 32, MinWidth = 32, MinHeight = 32, Padding = new Thickness(0),
            Style = (Style)Application.Current.Resources["SecondaryButtonStyle"], IsEnabled = !string.IsNullOrWhiteSpace(result.ProfileUrl)
        };
        ToolTipService.SetToolTip(openPageButton, T("Updates.OpenPage"));
        AutomationProperties.SetName(openPageButton, T("Updates.OpenPage"));
        openPageButton.Click += async (_, _) => await RunUiActionAsync("OpenExternalUrlAsync", () => OpenExternalUrlAsync(result.ProfileUrl, T("Common.OpenPageFailed")));
        actions.Children.Add(openPageButton);

        // Folder, old backup and removal live in the "More" menu.
        var more = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight };
        var openFolderItem = new MenuFlyoutItem
        {
            Text = T("Updates.OpenFolder"), Icon = new FontIcon { Glyph = "\uE838" }, IsEnabled = Directory.Exists(result.Path)
        };
        openFolderItem.Click += (_, _) =>
        {
            if (Directory.Exists(result.Path))
                Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{result.Path}\"", UseShellExecute = true });
        };
        more.Items.Add(openFolderItem);
        if (!string.IsNullOrWhiteSpace(result.BackupPath))
        {
            var backupItem = new MenuFlyoutItem { Text = T("Updates.OpenOldBackup"), Icon = new FontIcon { Glyph = "\uE81C" } };
            backupItem.Click += (_, _) => OpenDirectory(result.BackupPath, T("Updates.OldBackup"));
            more.Items.Add(backupItem);
        }
        more.Items.Add(new MenuFlyoutSeparator());
        var removeTrackingItem = new MenuFlyoutItem
        {
            Text = T("Updates.RemoveTracking"), Icon = new FontIcon { Glyph = "\uE74D" }, IsEnabled = !_modUpdatesInProgress.Contains(result.Path)
        };
        removeTrackingItem.Click += async (_, _) => await RunUiActionAsync("RemoveTrackedModRecordAsync", () => RemoveTrackedModRecordAsync(result));
        more.Items.Add(removeTrackingItem);
        var moreButton = new Button
        {
            Content = new FontIcon { Glyph = "\uE712", FontSize = 14 }, Width = 32, Height = 32, MinWidth = 32, MinHeight = 32, Padding = new Thickness(0),
            Style = (Style)Application.Current.Resources["SecondaryButtonStyle"], Flyout = more
        };
        ToolTipService.SetToolTip(moreButton, T("Updates.More"));
        AutomationProperties.SetName(moreButton, T("Updates.More"));
        actions.Children.Add(moreButton);
        Grid.SetColumn(actions, 3);
        grid.Children.Add(actions);

        return new Border { Style = (Style)Application.Current.Resources["InsetBorderStyle"], Padding = new Thickness(10), Child = grid };
    }

    private static string FormatDateForDisplay(DateTimeOffset? value)
    {
        return value.HasValue ? value.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "-";
    }

    private async Task CheckTrackedModUpdatesAsync(bool showDialogs)
    {
        if (_isCheckingModUpdates)
        {
            return;
        }

        if (_trackedModOrigins.Count == 0)
        {
            SetModUpdateStatus("还没有可检查更新的在线 Mod。", "There are no tracked online mods to check yet.");
            RefreshUpdatesPane();
            if (showDialogs)
            {
                await ShowMessageAsync(
                    T("Updates.DownloadAndExtractAtLeastOne"),
                    T("Updates.NoTrackedMods"));
            }
            return;
        }

        bool deduplicatedTrackedMods = DeduplicateTrackedModOrigins();
        if (deduplicatedTrackedMods)
        {
            SaveConfig();
            SaveShellConfig();
        }

        try
        {
            _isCheckingModUpdates = true;
            SetModUpdateStatus($"正在检查 {_trackedModOrigins.Count} 个已追踪 Mod 的更新...", $"Checking {_trackedModOrigins.Count} tracked mods for updates...");
            RefreshUpdatesPane();

            _trackedModUpdateResults.Clear();
            foreach (TrackedModOrigin origin in _trackedModOrigins.Values.OrderBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase).ToArray())
            {
                TrackedModUpdateResult result = await FetchTrackedModUpdateResultAsync(origin);
                _trackedModUpdateResults.Add(result);

                if (!string.IsNullOrWhiteSpace(result.PreviewUrl))
                {
                    origin.PreviewUrl = result.PreviewUrl;
                }

                if (!string.IsNullOrWhiteSpace(result.Title))
                {
                    origin.Title = result.Title;
                }

                if (!string.IsNullOrWhiteSpace(result.ProfileUrl))
                {
                    origin.ProfileUrl = result.ProfileUrl;
                    _modLinks[origin.Path] = result.ProfileUrl;
                }
            }

            _lastModUpdateCheckUtc = DateTimeOffset.UtcNow;
            SaveConfig();
            SaveShellConfig();

            int updateCount = _trackedModUpdateResults.Count(item => item.HasUpdate);
            int failedCount = _trackedModUpdateResults.Count(item => item.CheckFailed);
            SetModUpdateStatus(
                failedCount > 0 ? $"检查完成，发现 {updateCount} 个更新，{failedCount} 个检查失败，请重试。" :
                updateCount > 0
                    ? $"检查完成，发现 {updateCount} 个 Mod 有更新。"
                    : "检查完成，当前所有已追踪 Mod 都是最新状态。",
                failedCount > 0 ? $"Check complete: {updateCount} updates, {failedCount} checks failed. Please retry." : updateCount > 0
                    ? $"Check complete. {updateCount} tracked mods have updates."
                    : "Check complete. All tracked mods are up to date.");

            // The check is over; do not keep showing "Checking..." behind the result dialog.
            _isCheckingModUpdates = false;
            RefreshUpdatesPane();

            if (updateCount > 0)
            {
                await NotifyModUpdatesAvailableAsync();
            }
            else if (showDialogs)
            {
                await ShowMessageAsync(
                    updateCount > 0
                        ? T("Updates.CheckedModsAndFoundUpdates", _trackedModUpdateResults.Count, updateCount)
                        : failedCount > 0 ? T("Updates.ModChecksFailedPleaseRetry", failedCount)
                        : T("Updates.CheckedModsAndFoundNoNew", _trackedModUpdateResults.Count),
                    T("Updates.UpdateCheckComplete"));
            }
        }
        catch (Exception ex)
        {
            SetModUpdateStatus("检查已追踪 Mod 更新失败，请稍后重试。", "Failed to check tracked mod updates. Please try again later.");
            RefreshUpdatesPane();
            if (showDialogs)
            {
                await ShowMessageAsync(
                    T("Updates.FailedToCheckTrackedModUpdates") + ex.Message,
                    T("Updates.UpdateCheckFailed"));
            }
        }
        finally
        {
            _isCheckingModUpdates = false;
            RefreshUpdatesPane();
        }
    }

    private async Task<TrackedModUpdateResult> FetchTrackedModUpdateResultAsync(TrackedModOrigin origin)
    {
        OnlineModCard? latest = null;
        try
        {
            latest = await FetchGameBananaModCardAsyncV2(origin.ItemId);
        }
        catch (Exception ex)
        {
            LogApplicationIssue("Check tracked Mod update", ex);
        }

        if (latest is null)
            return new TrackedModUpdateResult
            {
                Path = origin.Path, ItemId = origin.ItemId, Title = origin.Title,
                ProfileUrl = origin.ProfileUrl, PreviewUrl = origin.PreviewUrl,
                LastKnownUpdatedAt = origin.LastKnownUpdatedAt, CheckFailed = true,
                StatusTextZh = "检查失败，未能取得最新版本，请重试。",
                StatusTextEn = "Check failed; latest version unavailable. Please retry."
            };

        DateTimeOffset? latestUpdatedAt = latest?.UpdatedAt;
        DateTimeOffset? recordedUpdatedAt = origin.LastKnownUpdatedAt;
        bool hasUpdate = latestUpdatedAt.HasValue
            && recordedUpdatedAt.HasValue
            && latestUpdatedAt.Value > recordedUpdatedAt.Value.AddMinutes(1);

        if (!recordedUpdatedAt.HasValue && latestUpdatedAt.HasValue)
        {
            origin.LastKnownUpdatedAt = latestUpdatedAt;
        }

        return new TrackedModUpdateResult
        {
            Path = origin.Path,
            ItemId = origin.ItemId,
            Title = string.IsNullOrWhiteSpace(latest?.Title) ? origin.Title : latest.Title,
            ProfileUrl = string.IsNullOrWhiteSpace(latest?.ProfileUrl) ? origin.ProfileUrl : latest.ProfileUrl,
            PreviewUrl = string.IsNullOrWhiteSpace(latest?.PreviewUrl) ? origin.PreviewUrl : latest.PreviewUrl,
            LastKnownUpdatedAt = recordedUpdatedAt ?? origin.LastKnownUpdatedAt,
            LatestUpdatedAt = latestUpdatedAt ?? recordedUpdatedAt ?? origin.LastKnownUpdatedAt,
            HasUpdate = hasUpdate,
            StatusTextZh = hasUpdate ? "检测到新版本，可前往页面查看。" : "当前没有检测到更新。",
            StatusTextEn = hasUpdate ? "A newer version was found. Open the page to review it." : "No update was detected."
        };
    }

    private void OnUpdateCheckIntervalSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!AppSelfUpdateEnabled || _isApplyingUpdateCheckIntervalSelection || UpdateCheckIntervalComboBox.SelectedItem is not ComboBoxItem { Tag: UpdateCheckInterval interval })
        {
            return;
        }

        try
        {
            _updateCheckInterval = interval;
            SaveShellConfig();
        }
        catch (Exception ex) { ReportUiActionFailure(nameof(OnUpdateCheckIntervalSelectionChanged), ex); }
    }

    private void OnModUpdateIntervalSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingModUpdateIntervalSelection || ModUpdateIntervalComboBox.SelectedItem is not ComboBoxItem { Tag: UpdateCheckInterval interval })
        {
            return;
        }

        try
        {
            _modUpdateCheckInterval = interval;
            SaveShellConfig();
        }
        catch (Exception ex) { ReportUiActionFailure(nameof(OnModUpdateIntervalSelectionChanged), ex); }
    }

    private async void OnCheckModUpdatesClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            await CheckTrackedModUpdatesAsync(showDialogs: true);
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnCheckModUpdatesClicked", ex);
        }
    }

    private async void OnCheckUpdatesClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            await CheckForUpdatesAsync(showDialogs: true);
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnCheckUpdatesClicked", ex);
        }
#if false

        if (_isCheckingUpdates)
        {
            return;
        }

        try
        {
            _isCheckingUpdates = true;
            CheckUpdatesButton.IsEnabled = false;
            ApplyLanguage();
            UpdateStatusTextBlock.Text = L("正在连接 GitHub Releases 检查更新...", "Checking GitHub Releases for updates...");

            using HttpResponseMessage response = await _httpClient.GetAsync(GitHubLatestReleaseApiUrl);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync();
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            string latestTag = root.TryGetProperty("tag_name", out JsonElement tagElement)
                ? tagElement.GetString() ?? string.Empty
                : string.Empty;
            string releaseUrl = root.TryGetProperty("html_url", out JsonElement urlElement)
                ? urlElement.GetString() ?? GitHubRepositoryUrl
                : GitHubRepositoryUrl;

            if (string.Equals(latestTag, AppVersion, StringComparison.OrdinalIgnoreCase))
            {
                UpdateStatusTextBlock.Text = L($"当前已经是最新版本：{latestTag}", $"You are already on the latest version: {latestTag}");
            }
            else if (!string.IsNullOrWhiteSpace(latestTag))
            {
                UpdateStatusTextBlock.Text = L($"检测到新版本：{latestTag}，当前是 {AppVersion}", $"New version detected: {latestTag}. Current version: {AppVersion}");

                bool openRelease = await ShowConfirmAsync(
                    L($"已检测到 GitHub 新版本：{latestTag}\n\n是否现在打开 Release 页面？", $"A newer GitHub release was found: {latestTag}\n\nOpen the release page now?"),
                    L("发现新版本", "Update available"));

                if (openRelease)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = releaseUrl,
                        UseShellExecute = true
                    });
                }
            }
            else
            {
                UpdateStatusTextBlock.Text = L("GitHub 已返回结果，但没有读到有效版本号。", "GitHub responded, but no valid version tag was found.");
            }
        }
        catch (Exception ex)
        {
            UpdateStatusTextBlock.Text = L("检查更新失败，请稍后重试。", "Update check failed. Please try again later.");
            await ShowMessageAsync(
                L("从 GitHub 检查更新失败：", "Failed to check updates from GitHub: ") + ex.Message,
                L("检查失败", "Check failed"));
        }
        finally
        {
            _isCheckingUpdates = false;
            CheckUpdatesButton.IsEnabled = true;
            ApplyLanguage();
        }
#endif
    }

    private async void OnInstallUpdateClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!AppSelfUpdateEnabled)
            {
                await OpenExternalUrlAsync(ApplicationReleasePolicy.ReleasePageUrl, T("Updates.FailedToOpenThePublicRelease"));
                return;
            }
            DetectLocalUpdatePackage();
            if (_localUpdatePackageVersion is not null && !string.IsNullOrWhiteSpace(_localUpdatePackagePath))
            {
                await PromptAndStartLocalUpdateAsync();
                return;
            }

            if (string.IsNullOrWhiteSpace(_latestReleaseUrl))
            {
                await CheckForUpdatesAsync(showDialogs: true);
                return;
            }

            if (!string.IsNullOrWhiteSpace(_latestReleasePackageUrl) && IsReleaseNewer(_latestReleaseTag ?? string.Empty))
            {
                await DownloadAndInstallApplicationUpdateAsync(new LatestReleaseInfo(
                    _latestReleaseTag ?? string.Empty,
                    _latestReleaseTitle ?? string.Empty,
                    _latestReleaseBody ?? string.Empty,
                    _latestReleasePublishedAt ?? string.Empty,
                    _latestReleaseUrl,
                    _latestReleasePackageUrl,
                    _latestReleaseSha256Url));
                return;
            }

            await OpenExternalUrlAsync(_latestReleaseUrl, T("Common.FailedToOpenTheUpdatePage"));
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnInstallUpdateClicked", ex);
        }
    }

    private void DetectLocalUpdatePackage()
    {
        _localUpdatePackagePath = null;
        _localUpdatePackageVersion = null;

        if (!AppSelfUpdateEnabled)
        {
            return;
        }

        string installRoot = GetInstallRootPath();
        if (!Directory.Exists(installRoot) || !TryParseVersion(AppVersion, out Version currentVersion))
        {
            return;
        }

        foreach (string packagePath in Directory.EnumerateFiles(installRoot, "*.zip", SearchOption.TopDirectoryOnly))
        {
            Match match = LocalUpdatePackageRegex.Match(Path.GetFileName(packagePath));
            if (!match.Success || !Version.TryParse(match.Groups["version"].Value, out Version? packageVersion))
            {
                continue;
            }

            if (packageVersion <= currentVersion || (_localUpdatePackageVersion is not null && packageVersion <= _localUpdatePackageVersion))
            {
                continue;
            }

            _localUpdatePackagePath = packagePath;
            _localUpdatePackageVersion = packageVersion;
        }

        if (_localUpdatePackageVersion is not null)
        {
            SetUpdateStatus(
                $"检测到本地更新包：v{_localUpdatePackageVersion}，配置将在更新时保留。",
                $"Local update v{_localUpdatePackageVersion} detected. Your configuration will be preserved.");
        }

        RefreshSettingsButtonLabels();
    }

    private static bool TryParseVersion(string value, out Version version)
    {
        return Version.TryParse(value.Trim().TrimStart('v', 'V'), out version!);
    }

    private string GetInstallRootPath()
    {
        var runtimeDirectory = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return runtimeDirectory.Name.Equals("WinUI3", StringComparison.OrdinalIgnoreCase) && runtimeDirectory.Parent is not null
            ? runtimeDirectory.Parent.FullName
            : runtimeDirectory.FullName;
    }

    private async Task PromptAndStartLocalUpdateAsync()
    {
        if (!AppSelfUpdateEnabled) { return; }
        if (_localUpdatePackageVersion is null || string.IsNullOrWhiteSpace(_localUpdatePackagePath))
        {
            return;
        }

        _hasPromptedForLocalUpdate = true;
        bool confirmed = await ShowConfirmAsync(
            T("Updates.LocalUpdateVWasDetectedThe", _localUpdatePackageVersion),
            T("Updates.InstallLocalUpdate"));

        if (!confirmed)
        {
            return;
        }

        await StartLocalUpdateAsync(_localUpdatePackagePath);
    }

    private async Task StartLocalUpdateAsync(string packagePath)
    {
        if (!AppSelfUpdateEnabled) { return; }
        if (IntegratedModManager.EndfieldLaunch.LaunchCommand.IsLaunchRunning())
        {
            // The window-less launch process runs this executable until the game exits.
            await ShowMessageAsync(
                T("Updates.AnEndfieldLaunchSessionIsStill"),
                T("Updates.UpdateUnavailable"));
            return;
        }

        try
        {
            string installRoot = GetInstallRootPath();
            string updaterSource = Path.Combine(installRoot, "LocalUpdateAgent.exe");
            string launcherPath = Path.Combine(installRoot, "IntegratedModManager.exe");
            if (!File.Exists(launcherPath))
            {
                launcherPath = Path.Combine(installRoot, "ModFolderCopier.exe");
            }

            if (!File.Exists(updaterSource))
            {
                await ShowMessageAsync(
                    T("Updates.LocalUpdateAgentExeIsMissingDownloadThe"),
                    T("Updates.UnableToUpdate"));
                return;
            }

            SaveConfig();
            SaveShellConfig();

            string updaterCopy = Path.Combine(Path.GetTempPath(), $"IntegratedModManagerUpdateAgent-{Guid.NewGuid():N}.exe");
            File.Copy(updaterSource, updaterCopy, overwrite: true);

            string arguments = string.Join(" ",
                "--package", QuoteCommandLineArgument(packagePath),
                "--install-root", QuoteCommandLineArgument(installRoot),
                "--process-id", Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
                "--launcher", QuoteCommandLineArgument(launcherPath),
                "--expected-version", _localUpdatePackageVersion?.ToString() ?? string.Empty,
                "--language", _currentLanguage == AppLanguage.EnUs ? "en" : "zh");

            Process.Start(new ProcessStartInfo
            {
                FileName = updaterCopy,
                Arguments = arguments,
                WorkingDirectory = installRoot,
                UseShellExecute = false
            });

            Close();
        }
        catch (Exception ex)
        {
            await ShowMessageAsync(
                T("Updates.FailedToStartTheLocalUpdate") + ex.Message,
                T("Common.UpdateFailed"));
        }
    }

    private static string QuoteCommandLineArgument(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private async Task CheckForUpdatesAsync(bool showDialogs, bool promptIfAvailable = false)
    {
        if (!AppSelfUpdateEnabled) { return; }
        if (_isCheckingUpdates)
        {
            return;
        }

        try
        {
            _isCheckingUpdates = true;
            CheckUpdatesButton.IsEnabled = false;
            RefreshSettingsPane();
            SetUpdateStatus("正在连接 GitHub Releases 检查更新...", "Checking GitHub Releases for updates...");

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            LatestReleaseInfo release = await FetchLatestReleaseInfoAsync(timeout.Token);
            ApplyLatestReleaseInfo(release);

            if (!TryParseVersion(release.Tag, out _))
            {
                SetUpdateStatus("GitHub 返回了结果，但没有读到有效版本号。", "GitHub responded, but no valid version tag was found.");
            }
            else if (!IsReleaseNewer(release.Tag))
            {
                SetUpdateStatus($"当前已经是最新版本：{AppVersion}", $"You are already on the latest version: {AppVersion}");
                if (showDialogs)
                {
                    ShowAppNotification("当前已经是最新版本。", "The app is up to date.", InfoBarSeverity.Success);
                }
            }
            else
            {
                SetUpdateStatus($"检测到新版本：{release.Tag}，当前是 {AppVersion}", $"New version detected: {release.Tag}. Current version: {AppVersion}");
                if (showDialogs || promptIfAvailable)
                {
                    bool install = await ShowConfirmAsync(
                        T("Updates.VersionIsAvailableDownloadInstallAnd", release.Tag),
                        T("Updates.AppUpdateAvailable"));
                    if (install)
                    {
                        await DownloadAndInstallApplicationUpdateAsync(release);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            SetUpdateStatus("检查更新失败，请稍后重试。", "Update check failed. Please try again later.");
            if (showDialogs)
            {
                await ShowMessageAsync(
                    T("Updates.FailedToCheckUpdatesFromGitHub") + ex.Message,
                    T("Updates.CheckFailed"));
            }
        }
        finally
        {
            _isCheckingUpdates = false;
            CheckUpdatesButton.IsEnabled = AppSelfUpdateEnabled;
            RefreshSettingsPane();
        }
    }
}
