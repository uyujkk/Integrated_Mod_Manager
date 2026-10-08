using IntegratedModManager.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private Button? _exportCombinationBundleButton, _importCombinationBundleButton, _cancelCombinationBundleButton;
    private CancellationTokenSource? _bundleOperationCancellation;

    private void InitializeCombinationBundleActions()
    {
        // Keep Create in its known owner. Reparenting it before the grid is attached
        // makes Parent unreliable and can throw when it is added to a second owner.
        // Bundle commands span the workspace, not the constrained preset-list footer.
        var actions = new StackPanel { Spacing = 8, Orientation = Orientation.Horizontal };
        _exportCombinationBundleButton = RepositoryButton("\uE896", async (_, _) => await ExportCombinationBundleAsync());
        _importCombinationBundleButton = RepositoryButton("\uE898", async (_, _) => await ImportCombinationBundleAsync());
        _cancelCombinationBundleButton = RepositoryButton("\uE711", (_, _) =>
        {
            _bundleOperationCancellation?.Cancel();
            RefreshCombinationBundleActions();
        });
        foreach (Button button in new[] { _exportCombinationBundleButton, _importCombinationBundleButton, _cancelCombinationBundleButton })
        {
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            actions.Children.Add(button);
        }
        Grid.SetColumnSpan(actions, 3);
        _combinationWorkspace!.Children.Add(actions);
        RefreshCombinationBundleActions();
    }

    private void RefreshCombinationBundleActions()
    {
        if (_exportCombinationBundleButton is null) return;
        bool available = !_applyingCombinationProfile && !_restoringPersistentRuntime
            && GetSelectedRepository() is { SourcePath.Length: > 0 } repository && Directory.Exists(repository.SourcePath);
        SetLabel(_exportCombinationBundleButton, "\uE896", L("导出组合包", "Export Bundle"));
        SetLabel(_importCombinationBundleButton!, "\uE898", L("安装组合包", "Install Bundle"));
        SetLabel(_cancelCombinationBundleButton!, "\uE711", L("取消操作", "Cancel Task"));
        _exportCombinationBundleButton.IsEnabled = available && GetSelectedConfigurationProfile() is not null;
        _importCombinationBundleButton!.IsEnabled = available;
        _cancelCombinationBundleButton!.Visibility = _bundleOperationCancellation is null ? Visibility.Collapsed : Visibility.Visible;
        _cancelCombinationBundleButton.IsEnabled = _bundleOperationCancellation is { IsCancellationRequested: false };

        static void SetLabel(Button button, string glyph, string text)
        {
            button.Content = RepositoryIconLabel(glyph, text);
            ToolTipService.SetToolTip(button, text);
            AutomationProperties.SetName(button, text);
        }
    }

    private IProgress<CombinationBundleProgress> BundleProgress(CancellationToken token, string operation) =>
        new Progress<CombinationBundleProgress>(info =>
        {
            if (!_onlineWindowClosed && !token.IsCancellationRequested && _bundleOperationCancellation?.Token == token)
                UpdateProgress(info.Percent, operation + (info.RelativePath.Length == 0 ? "" : " · " + info.RelativePath));
        });

    private async Task ExportCombinationBundleAsync()
    {
        if (_applyingCombinationProfile || _restoringPersistentRuntime || GetSelectedRepository() is not { } repository
            || GetSelectedConfigurationProfile() is not { } profile) return;
        string root = repository.SourcePath, name = profile.Name, game = repository.OnlineGameName;
        bool includeState = profile.IncludesPersistentState;
        // Freeze the saved profile, not live loader values, before any picker/dialog awaits.
        CombinationPresetState[] mods;
        try
        {
            var states = profile.PersistentStates.ToDictionary(state => state.ModRelativePath, StringComparer.OrdinalIgnoreCase);
            if (includeState && !states.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(profile.ModRelativePaths))
                throw new InvalidDataException(L("预设参数与 Mod 清单不完整匹配，请重新保存组合。", "Saved state and Mod list do not match. Save the combination again."));
            mods = profile.ModRelativePaths.Select(path => new CombinationPresetState(path,
                includeState ? states[path].Values.ToArray() : [])).ToArray();
        }
        catch (Exception ex)
        {
            await ReportBundleErrorAsync(ex, false);
            return;
        }
        using var cancellation = new CancellationTokenSource();
        _bundleOperationCancellation = cancellation;
        SetCombinationBusy(true);
        try
        {
            if (!await ShowConfirmAsync(L(
                "导出所选预设保存的参数与对应 Mod 文件，不读取游戏当前状态。需要最新参数时，请先更新保存组合。会包含这些 Mod 目录中的全部文件；压缩包不加密，请检查私人文件，并确认作者允许再分发。",
                "Export saved preset state, not current game state. Update the saved combination first if needed. All files in these Mod folders are included. Bundles are unencrypted: check private files and authors' redistribution permissions before sharing."),
                L("导出组合包", "Export Bundle"))) return;
            cancellation.Token.ThrowIfCancellationRequested();
            string? folder = await PickFolderAsync();
            cancellation.Token.ThrowIfCancellationRequested();
            if (folder is null || _onlineWindowClosed) return;
            string output = Path.Combine(folder, $"Combination-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}.imm-preset.zip");
            var progress = BundleProgress(cancellation.Token, L("导出组合包", "Exporting bundle"));
            await Task.Run(() => CombinationBundleService.Export(root, output, name, game, includeState, mods,
                cancellation.Token, progress: progress));
            await ReportOnlineActionErrorAsync(L($"已生成组合包：{mods.Length} 个 Mod · {mods.Sum(mod => mod.Values.Count)} 个保存参数。\n{output}",
                $"Bundle created: {mods.Length} Mods · {mods.Sum(mod => mod.Values.Count)} saved values.\n{output}"), L("导出完成", "Export Complete"));
        }
        catch (OperationCanceledException) { if (!_onlineWindowClosed) ShowAppNotification("已取消导出组合包。", "Bundle export cancelled."); }
        catch (Exception ex) { await ReportBundleErrorAsync(ex, false); }
        finally
        {
            _bundleOperationCancellation = null;
            if (!_onlineWindowClosed) SetCombinationBusy(false);
        }
    }

    private async Task ImportCombinationBundleAsync()
    {
        if (_applyingCombinationProfile || _restoringPersistentRuntime || GetSelectedRepository() is not { } repository) return;
        string root = repository.SourcePath, repositoryId = repository.Id;
        using var cancellation = new CancellationTokenSource();
        _bundleOperationCancellation = cancellation;
        SetCombinationBusy(true);
        CombinationBundleImport? import = null;
        bool restore = false;
        try
        {
            string? archive = await PickFileAsync([".zip"]);
            cancellation.Token.ThrowIfCancellationRequested();
            if (archive is null || _onlineWindowClosed) return;
            var validationProgress = BundleProgress(cancellation.Token, L("验证组合包", "Validating bundle"));
            import = await Task.Run(() => CombinationBundleService.PrepareImport(archive, root,
                CombinationBundleService.CreateStagingParent(root), cancellation.Token,
                progress: validationProgress));
            cancellation.Token.ThrowIfCancellationRequested();
            if (_onlineWindowClosed) return;
            CombinationBundleManifest manifest = import.Manifest;
            var confirmation = await ConfirmBundleInstallAsync(import, repository.OnlineGameName);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!confirmation.Install || _onlineWindowClosed) return;
            if (GetSelectedRepository() is not { } current || current.Id != repositoryId || current.SourcePath != root)
                throw new IOException(L("仓库发生变化，请重新选择组合包。", "Repository changed. Select the bundle again."));
            var progress = BundleProgress(cancellation.Token, L("安装组合包", "Installing bundle"));
            await Task.Run(() => import.InstallFiles(cancellation.Token, progress));
            cancellation.Token.ThrowIfCancellationRequested();
            var profile = new ModConfigurationProfile
            {
                RepositoryId = repositoryId, Name = UniqueBundleProfileName(manifest.Name),
                ModRelativePaths = manifest.Mods.Select(mod => mod.RelativePath.Replace('/', Path.DirectorySeparatorChar)).ToList(),
                IncludesPersistentState = manifest.IncludesPersistentState,
                PersistentStates = manifest.IncludesPersistentState ? manifest.Mods.Select(mod => new ModConfigurationPersistentState
                {
                    ModRelativePath = mod.RelativePath.Replace('/', Path.DirectorySeparatorChar), Values = mod.Values.ToList()
                }).ToList() : []
            };
            string? previousSelection = _selectedConfigurationProfileId;
            _configurationProfiles.Add(profile);
            _selectedConfigurationProfileId = profile.Id;
            try
            {
                // No await/cancel gap between atomic configuration publication and file ownership commit.
                SaveShellConfig(throwOnError: true);
                import.Complete();
            }
            catch
            {
                _configurationProfiles.Remove(profile);
                _selectedConfigurationProfileId = previousSelection;
                throw;
            }
            restore = confirmation.Restore;
            if (!_onlineWindowClosed)
            {
                await RefreshListsAsync();
                ShowAppNotification($"已安装并保存组合“{profile.Name}”。", $"Installed and saved combination '{profile.Name}'.");
            }
        }
        catch (OperationCanceledException) { if (!_onlineWindowClosed) ShowAppNotification("已取消安装组合包。", "Bundle installation cancelled."); }
        catch (Exception ex) { restore = false; await ReportBundleErrorAsync(ex, import?.IsCommitted == true); }
        finally
        {
            import?.Dispose();
            _bundleOperationCancellation = null;
            if (!_onlineWindowClosed)
            {
                SetCombinationBusy(false);
                if (import?.CleanupWarnings.Count > 0)
                    await ReportOnlineActionErrorAsync(string.Join("\n", import.CleanupWarnings), L("部分文件需手动检查", "Some Files Need Inspection"));
            }
        }
        // Restore is a separate approved transaction: deploy the set, then patch selected d3dx_user.ini keys.
        // Cancelling/failing that step leaves the imported files and saved profile available for retry.
        if (restore && import?.IsCommitted == true && !_onlineWindowClosed) await RestoreCombinationProfileAsync();
    }

    private string UniqueBundleProfileName(string name)
    {
        var used = GetCurrentRepositoryProfiles().Select(profile => profile.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(name)) return name;
        for (int i = 2; ; i++) if (!used.Contains($"{name} ({i})")) return $"{name} ({i})";
    }

    private async Task<(bool Install, bool Restore)> ConfirmBundleInstallAsync(CombinationBundleImport import, string currentGame)
    {
        var manifest = import.Manifest;
        DialogViewport viewport = DialogViewportPolicy.Selection(RootGrid.ActualWidth, RootGrid.ActualHeight);
        var panel = new Grid { RowSpacing = 10, MaxWidth = viewport.MaxWidth, Height = viewport.ContentHeight };
        panel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        string summary = L($"{manifest.Name} · {manifest.Mods.Count} 个 Mod · {manifest.Mods.Sum(mod => mod.Values.Count)} 个参数\n",
            $"{manifest.Name} · {manifest.Mods.Count} Mods · {manifest.Mods.Sum(mod => mod.Values.Count)} values\n");
        summary += L("先安装到当前仓库并保存预设；原 Mod INI 不改写。相同文件复用，不同内容阻止安装。游戏标记仅供参考，不保证版本兼容。",
            "Install into the current repository and save the preset first. Mod INIs are unchanged. Identical files are reused; different contents block installation. Game labels are hints, not compatibility guarantees.");
        if (manifest.Game.Length > 0) summary += "\n" + L("组合包游戏：", "Bundle game: ") + manifest.Game;
        if (manifest.Game.Length > 0 && currentGame.Length > 0 && !manifest.Game.Equals(currentGame, StringComparison.OrdinalIgnoreCase))
            summary += L("\n注意：与当前仓库游戏标记不同，请确认没有选错仓库。", "\nWarning: game label differs from this repository. Check your selection.");
        if (manifest.Mods.Count == 0) summary += L("\n空组合：恢复时会移除当前仓库中能识别的部署。", "\nEmpty set: restoring it removes recognized deployments from this repository.");
        panel.Children.Add(new TextBlock { Text = summary, TextWrapping = TextWrapping.Wrap });
        var list = new ListView
        {
            SelectionMode = ListViewSelectionMode.None, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            ItemsSource = import.Preview.Select(item => new CombinationPreviewRow(item.RelativePath, "", item.Disposition switch
            {
                CombinationBundleDisposition.Install => L("新安装", "New install"),
                CombinationBundleDisposition.Reuse => L("文件完全相同，复用", "Identical files, reuse"),
                _ => L("路径或内容冲突，阻止安装", "Path/content conflict, blocked")
            })).ToArray(),
            ItemTemplate = (DataTemplate)XamlReader.Load("""
                <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                  <StackPanel Padding="8" Spacing="4">
                    <TextBlock Text="{Binding Title}" TextWrapping="Wrap"/>
                    <TextBlock Text="{Binding ValueText}" FontSize="12" TextWrapping="Wrap"/>
                  </StackPanel>
                </DataTemplate>
                """)
        };
        Grid.SetRow(list, 1); panel.Children.Add(list);
        var restore = new CheckBox { Content = L("安装后进入组合与参数恢复确认", "Review deployment and state restore after installation"),
            IsChecked = true, IsEnabled = import.CanInstall };
        Grid.SetRow(restore, 2); panel.Children.Add(restore);
        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot, RequestedTheme = RootGrid.ActualTheme,
            Title = L("安装组合包预览", "Bundle Installation Preview"), Content = panel,
            PrimaryButtonText = L("安装并保存预设", "Install and Save Preset"), CloseButtonText = L("取消", "Cancel"),
            IsPrimaryButtonEnabled = import.CanInstall, DefaultButton = ContentDialogButton.Close
        };
        dialog.Resources["ContentDialogMaxWidth"] = viewport.MaxWidth;
        return (await dialog.ShowAsync() == ContentDialogResult.Primary, restore.IsChecked == true);
    }

    private async Task ReportBundleErrorAsync(Exception ex, bool committed)
    {
        LogApplicationIssue("Combination bundle", ex);
        await ReportOnlineActionErrorAsync(L(committed
            ? "组合已安装并保存，但后续刷新未完成；请刷新仓库后重试恢复。\n"
            : "组合包操作未完成，已有 Mod 不会被覆盖。\n", committed
            ? "The combination was installed and saved, but refresh did not finish. Refresh the repository before restoring.\n"
            : "Bundle operation did not finish; existing Mods are not overwritten.\n") + ex.Message,
            L("组合包提示", "Combination Bundle"));
    }
}
