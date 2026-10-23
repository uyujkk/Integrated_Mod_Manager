using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using IntegratedModManager.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
            LatestReleaseTitleTextBlock.Text = L($"独立测试版：{AppVersion}", $"Standalone beta: {AppVersion}");
            LatestReleaseMetaTextBlock.Text = L("手动下载 · 与稳定版分开解压使用", "Manual download · extract separately from the stable app");
            UpdateNotesTextBlock.Text = L(
                "Mod 更新修复测试版：修复检查频率切换崩溃，新增更新弹窗、下载并替换和旧版备份。\n本地测试包未发布；软件自身更新已关闭，Mod 更新仍可使用。",
                "Mod update test build: fixes interval selection crashes and adds update prompts, download and replace, and old-version backups.\nThis local test package is unpublished; app self-updates are disabled and Mod updates remain available.");
            return;
        }
        string latestTag = string.IsNullOrWhiteSpace(_latestReleaseTag) ? L("尚未检查", "Not checked yet") : _latestReleaseTag!;
        string latestTitle = string.IsNullOrWhiteSpace(_latestReleaseTitle)
            ? L("最新版本信息会显示在这里", "Latest release details will appear here")
            : NormalizeUiText(_latestReleaseTitle);

        string publishedText = TryParseDateTimeOffset(_latestReleasePublishedAt) is DateTimeOffset publishedAt
            ? publishedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : L("未知时间", "Unknown time");

        string lastCheckedText = _lastUpdateCheckUtc.HasValue
            ? _lastUpdateCheckUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : L("尚未检查", "Not checked yet");

        LatestReleaseTitleTextBlock.Text = L($"最新版本：{latestTag}", $"Latest release: {latestTag}");
        LatestReleaseMetaTextBlock.Text = L(
            $"标题：{latestTitle}\n发布时间：{publishedText}\n上次检查：{lastCheckedText}",
            $"Title: {latestTitle}\nPublished: {publishedText}\nLast checked: {lastCheckedText}");
        UpdateNotesTextBlock.Text = string.IsNullOrWhiteSpace(_latestReleaseBody)
            ? L("检查到新版本后，这里会显示 GitHub Release 的更新内容。", "GitHub release notes will appear here after an update check.")
            : NormalizeUiText(_latestReleaseBody);
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
        UpdatesTitleTextBlock.Text = L("Mod 更新", "Mod Updates");
        UpdatesSubtitleTextBlock.Text = L("检查已安装 Mod 的新版本，并集中处理下载、配置方案和安装恢复。", "Check installed mods for new versions and manage downloads, profiles, and recovery in one place.");
        ConfigurationProfilesTitleTextBlock.Text = L("Mod 组合预设（实验）", "Mod Combination Presets (Experimental)");
        ConfigurationProfilesHintTextBlock.Text = L("保存启用组合和持久参数；恢复时先部署组合，再定向回写加载器。", "Save enabled Mods and persistent state; restore deployment first, then loader parameters.");
        CaptureCombinationStateCheckBox.Content = L("新建/更新时同时保存 d3dx_user.ini 参数", "Include d3dx_user.ini parameters when saving/updating");
        BrowseCombinationUserIniButton.Content = L("选择文件", "Browse");
        CreateConfigurationProfileButton.Content = L("新建方案", "New Profile");
        UpdateConfigurationProfileButton.Content = L("更新方案", "Update Profile");
        ApplyConfigurationProfileButton.Content = L("预览并恢复组合", "Preview and Restore");
        DeleteConfigurationProfileButton.Content = new FontIcon { Glyph = "\uE74D", FontSize = 16 };
        ToolTipService.SetToolTip(DeleteConfigurationProfileButton, L("删除选中的配置方案", "Delete the selected profile"));
        InstallSafetyTitleTextBlock.Text = L("安装安全", "Install Safety");
        InstallSafetyHintTextBlock.Text = L("安装前检查文件冲突；复制、移除和方案切换均可通过备份恢复。", "Check file conflicts before installation; copies, removals, and profile changes can be recovered from backups.");
        ConflictDetectionToggleSwitch.Header = L("安装前检测冲突", "Detect conflicts before install");
        ConflictDetectionToggleSwitch.OnContent = L("开启", "On");
        ConflictDetectionToggleSwitch.OffContent = L("关闭", "Off");
        ConflictDetectionToggleSwitch.IsOn = _enableConflictDetection;
        RollbackLastInstallButton.Content = L("撤销上次 Mod 操作", "Undo Last Mod Change");
        // "Downloads" maps to the download-count label in the shared table; use a distinct key.
        DownloadTaskCenterTitleTextBlock.Text = L("下载任务", "Download tasks");
        DownloadTaskCenterHintTextBlock.Text = L("显示在线 Mod 的下载与解压进度。", "Shows online mod download and extraction progress.");
        ClearDownloadTasksButton.Content = L("清除已完成", "Clear Finished");
        RefreshBackupManagerText();
        _ = RefreshInstallBackupListAsync();
        RefreshConfigurationProfiles();
        RefreshDownloadTaskCenter();
        TrackedModSettingsTitleTextBlock.Text = L("检查方式", "Check Settings");
        TrackedModSettingsHintTextBlock.Text = L("按设定频率在启动时检查，也可立即刷新当前仓库。", "Check on startup at the selected interval, or refresh this repository now.");
        ModUpdateIntervalLabelTextBlock.Text = L("检查频率", "Check interval");
        ModUpdateIntervalHintTextBlock.Text = L("仅检查已保存来源 ID 的在线安装 Mod。", "Only online-installed mods with a saved source ID are checked.");
        CheckModUpdatesButton.Content = _isCheckingModUpdates ? L("检查中...", "Checking...") : L("手动检查更新", "Check Now");
        TrackedModsTitleTextBlock.Text = L("已追踪 Mod", "Tracked Mods");

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
            : L("尚未检查", "Not checked yet");
        int trackedCount = _trackedModOrigins.Count;
        int updateCount = _trackedModUpdateResults.Count(item => item.HasUpdate);
        TrackedModsSummaryTextBlock.Text = L(
            $"已追踪 {trackedCount} 个 Mod，上次检查：{lastCheckedText}，发现可更新 {updateCount} 个。",
            $"{trackedCount} tracked mods. Last checked: {lastCheckedText}. Updates available: {updateCount}.");

        PopulateModUpdateIntervalOptions();
        TrackedModsListPanel.Children.Clear();
        TrackedModsListPanel.RowDefinitions.Clear();

        IEnumerable<TrackedModUpdateResult> orderedResults = _trackedModUpdateResults
            .OrderByDescending(item => item.HasUpdate)
            .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase);

        if (!orderedResults.Any())
        {
            if (_trackedModOrigins.Count == 0)
            {
                TrackedModsListPanel.Children.Add(CreateInfoCard(
                    L("还没有可追踪的在线 Mod", "No tracked online mods yet"),
                    L("从在线模块下载并解压到仓库后，这里会自动记录来源链接、图片和远程 ID。", "After downloading and extracting from the online browser, this page will automatically track the source link, preview image, and remote ID.")));
            }
            else
            {
                foreach (TrackedModOrigin origin in _trackedModOrigins.Values.OrderBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase))
                {
                    TrackedModsListPanel.Children.Add(CreateTrackedModUpdateCard(new TrackedModUpdateResult
                    {
                        Path = origin.Path,
                        Title = origin.Title,
                        ProfileUrl = origin.ProfileUrl,
                        PreviewUrl = origin.PreviewUrl,
                        ItemId = origin.ItemId,
                        LastKnownUpdatedAt = origin.LastKnownUpdatedAt,
                        LatestUpdatedAt = origin.LastKnownUpdatedAt,
                        HasUpdate = false,
                        StatusTextZh = string.Empty,
                        StatusTextEn = string.Empty
                    }, isChecked: false));
                }
            }

            ApplyTrackedModsListLayout();
            return;
        }

        foreach (TrackedModUpdateResult result in orderedResults)
        {
            TrackedModsListPanel.Children.Add(CreateTrackedModUpdateCard(result));
        }

        ApplyTrackedModsListLayout();
    }

    private void ApplyTrackedModsListLayout()
    {
        if (TrackedModsListPanel is null
            || TrackedModsScrollViewer is null
            || TrackedModsPrimaryColumn is null
            || TrackedModsSecondaryColumn is null)
        {
            return;
        }

        int columnCount = _useWideTrackedModsLayout ? 2 : 1;
        TrackedModsPrimaryColumn.Width = new GridLength(1, GridUnitType.Star);
        TrackedModsSecondaryColumn.Width = _useWideTrackedModsLayout
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(0);

        TrackedModsListPanel.RowDefinitions.Clear();
        int childCount = TrackedModsListPanel.Children.Count;
        int rowCount = Math.Max(1, (int)Math.Ceiling(childCount / (double)columnCount));
        // The list grows with the page scroll instead of a nested fixed-height scroller.
        TrackedModsScrollViewer.MaxHeight = double.PositiveInfinity;
        TrackedModsScrollViewer.Height = double.NaN;
        for (int row = 0; row < rowCount; row++)
        {
            TrackedModsListPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        for (int index = 0; index < childCount; index++)
        {
            FrameworkElement child = (FrameworkElement)TrackedModsListPanel.Children[index];
            Grid.SetRow(child, index / columnCount);
            Grid.SetColumn(child, index % columnCount);
            Grid.SetColumnSpan(child, childCount == 1 && columnCount == 2 ? 2 : 1);
            child.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
    }

    private UIElement CreateTrackedModUpdateCard(TrackedModUpdateResult result, bool isChecked = true)
    {
        Border border = new()
        {
            Style = (Style)Application.Current.Resources["InsetBorderStyle"],
            Padding = new Thickness(14)
        };

        Grid grid = new() { ColumnSpacing = 14 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Border previewHost = new()
        {
            Style = (Style)Application.Current.Resources["InsetBorderStyle"],
            MinHeight = 72,
            Height = 72
        };
        if (!string.IsNullOrWhiteSpace(result.PreviewUrl))
        {
            var previewImage = new Image
            {
                Stretch = Stretch.UniformToFill
            };
            previewHost.Child = previewImage;
            _ = SetCachedOnlineImageAsync(previewImage, result.PreviewUrl);
        }
        else
        {
            previewHost.Child = new TextBlock
            {
                Text = L("暂无预览", "No preview"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Style = (Style)Application.Current.Resources["CaptionTextStyle"]
            };
        }

        Grid.SetColumn(previewHost, 0);
        grid.Children.Add(previewHost);

        StackPanel content = new() { Spacing = 6 };
        // Title and status badge share a row; the badge never squeezes the title off the card.
        Grid heading = new() { ColumnSpacing = 10 };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.Children.Add(new TextBlock
        {
            Text = result.Title,
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        Brush statusBrush = !isChecked || result.CheckFailed ? (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] : result.HasUpdate ? MissingBrush : CopiedBrush;
        Border badge = new()
        {
            CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 3, 8, 3), VerticalAlignment = VerticalAlignment.Top,
            BorderBrush = statusBrush, BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = !isChecked ? L("未检查", "Not checked") : result.CheckFailed ? L("检查失败", "Check failed")
                    : result.HasUpdate ? L("有更新", "Update") : L("已是最新", "Up to date"),
                Foreground = statusBrush, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            }
        };
        Grid.SetColumn(badge, 1);
        heading.Children.Add(badge);
        content.Children.Add(heading);
        var pathText = new TextBlock
        {
            Text = $"{L("本地路径", "Local path")}: {TrimPreviewPath(result.Path)}",
            Style = (Style)Application.Current.Resources["CaptionTextStyle"],
            TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1
        };
        ToolTipService.SetToolTip(pathText, result.Path);
        content.Children.Add(pathText);
        content.Children.Add(new TextBlock
        {
            Text = $"{L("记录时间", "Recorded")}: {FormatDateForDisplay(result.LastKnownUpdatedAt)}    {L("最新时间", "Latest")}: {FormatDateForDisplay(result.LatestUpdatedAt)}",
            Style = (Style)Application.Current.Resources["MutedTextStyle"],
            TextWrapping = TextWrapping.Wrap
        });
        // The badge already says "not checked"; only real results get a status line.
        if (!string.IsNullOrWhiteSpace(result.StatusTextZh)) content.Children.Add(new TextBlock
        {
            Text = L(result.StatusTextZh, result.StatusTextEn),
            Foreground = statusBrush,
            FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            TextWrapping = TextWrapping.Wrap
        });

        // At most three controls per row, so narrow or two-column layouts never clip buttons;
        // folder, backup and removal live in the "More" menu.
        StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (result.HasUpdate)
        {
            var replaceButton = new Button
            {
                Content = L("下载并替换", "Download and replace"),
                Style = (Style)Application.Current.Resources["PrimaryButtonStyle"], MinHeight = 36,
                IsEnabled = !_modUpdatesInProgress.Contains(result.Path) && Directory.Exists(result.Path)
            };
            replaceButton.Click += async (_, _) => await RunUiActionAsync("Download and replace Mod", () => DownloadAndReplaceTrackedModAsync(result));
            actions.Children.Add(replaceButton);
        }
        Button openPageButton = new()
        {
            Content = L("打开页面", "Open Page"),
            Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
            MinHeight = 36,
            IsEnabled = !string.IsNullOrWhiteSpace(result.ProfileUrl)
        };
        openPageButton.Click += async (_, _) => await RunUiActionAsync("OpenExternalUrlAsync", () => OpenExternalUrlAsync(result.ProfileUrl, L("打开页面失败", "Open page failed")));
        actions.Children.Add(openPageButton);

        var more = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedLeft };
        var openFolderItem = new MenuFlyoutItem
        {
            Text = L("打开本地目录", "Open Folder"), Icon = new FontIcon { Glyph = "" },
            IsEnabled = Directory.Exists(result.Path)
        };
        openFolderItem.Click += (_, _) =>
        {
            if (Directory.Exists(result.Path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{result.Path}\"",
                    UseShellExecute = true
                });
            }
        };
        more.Items.Add(openFolderItem);
        if (!string.IsNullOrWhiteSpace(result.BackupPath))
        {
            var backupItem = new MenuFlyoutItem { Text = L("打开旧版备份", "Open old backup"), Icon = new FontIcon { Glyph = "" } };
            backupItem.Click += (_, _) => OpenDirectory(result.BackupPath, L("旧版备份", "Old backup"));
            more.Items.Add(backupItem);
        }
        more.Items.Add(new MenuFlyoutSeparator());
        var removeTrackingItem = new MenuFlyoutItem
        {
            Text = L("删除记录", "Remove Tracking"), Icon = new FontIcon { Glyph = "" },
            IsEnabled = !_modUpdatesInProgress.Contains(result.Path)
        };
        removeTrackingItem.Click += async (_, _) => await RunUiActionAsync("RemoveTrackedModRecordAsync", () => RemoveTrackedModRecordAsync(result));
        more.Items.Add(removeTrackingItem);
        actions.Children.Add(new DropDownButton
        {
            // No Button style here: it would replace the template and drop the chevron.
            Content = L("更多", "More"), Flyout = more, MinHeight = 36
        });

        content.Children.Add(actions);
        Grid.SetColumn(content, 1);
        grid.Children.Add(content);

        border.Child = grid;
        return border;
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
                    L("请先从在线模块下载并解压至少一个 Mod，系统才会记录来源并检查更新。", "Download and extract at least one mod from the online browser first so the app can track and check updates."),
                    L("没有可检查的 Mod", "No tracked mods"));
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
                        ? L($"已检查 {_trackedModUpdateResults.Count} 个 Mod，发现 {updateCount} 个有更新。", $"Checked {_trackedModUpdateResults.Count} mods and found {updateCount} updates.")
                        : failedCount > 0 ? L($"{failedCount} 个 Mod 检查失败，请重试。", $"{failedCount} Mod checks failed. Please retry.")
                        : L($"已检查 {_trackedModUpdateResults.Count} 个 Mod，没有发现新更新。", $"Checked {_trackedModUpdateResults.Count} mods and found no new updates."),
                    L("更新检查完成", "Update check complete"));
            }
        }
        catch (Exception ex)
        {
            SetModUpdateStatus("检查已追踪 Mod 更新失败，请稍后重试。", "Failed to check tracked mod updates. Please try again later.");
            RefreshUpdatesPane();
            if (showDialogs)
            {
                await ShowMessageAsync(
                    L("检查已追踪 Mod 更新失败：", "Failed to check tracked mod updates: ") + ex.Message,
                    L("更新检查失败", "Update check failed"));
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
        catch
        {
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
                await OpenExternalUrlAsync(ApplicationReleasePolicy.ReleasePageUrl, L("打开公开发布页失败", "Failed to open the public release page"));
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

            await OpenExternalUrlAsync(_latestReleaseUrl, L("打开更新页面失败", "Failed to open the update page"));
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
            L(
                $"检测到本地更新包 v{_localUpdatePackageVersion}。\n\n程序将退出并自动完成更新，然后重新启动。现有仓库、路径、语言、主题、快捷键和 Mod 链接配置都会保留。\n\n是否现在更新？",
                $"Local update v{_localUpdatePackageVersion} was detected.\n\nThe app will close, update itself, and restart. Existing repositories, paths, language, theme, shortcuts, and mod links will be preserved.\n\nUpdate now?"),
            L("安装本地更新", "Install Local Update"));

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
                L("终末地启动会话仍在运行，更新需要替换正在使用的程序文件。请先退出游戏再更新。", "An Endfield launch session is still running and the update must replace files it uses. Exit the game, then update."),
                L("暂不能更新", "Update unavailable"));
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
                    L("更新组件 LocalUpdateAgent.exe 不存在，请重新下载完整发布包。", "LocalUpdateAgent.exe is missing. Download the complete release package again."),
                    L("无法开始更新", "Unable to Update"));
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
                L("启动本地更新失败：", "Failed to start the local update: ") + ex.Message,
                L("更新失败", "Update Failed"));
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
                        L($"发现新版本 {release.Tag}。\n\n是否自动下载、安装并重启软件？本地仓库、路径和界面设置会保留。",
                          $"Version {release.Tag} is available.\n\nDownload, install, and restart automatically? Local repositories, paths, and interface settings will be preserved."),
                        L("软件更新可用", "App Update Available"));
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
                    L("从 GitHub 检查更新失败：", "Failed to check updates from GitHub: ") + ex.Message,
                    L("检查失败", "Check failed"));
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
