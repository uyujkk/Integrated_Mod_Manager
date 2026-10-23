using IntegratedModManager.Core.Layout;
using IntegratedModManager.Core.Presets;
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
        SetLabel(_exportCombinationBundleButton, "\uE896", T("CombinationBundles.ExportBundle"));
        SetLabel(_importCombinationBundleButton!, "\uE898", T("CombinationBundles.InstallBundle"));
        SetLabel(_cancelCombinationBundleButton!, "\uE711", T("CombinationBundles.CancelTask"));
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
                throw new InvalidDataException(T("CombinationBundles.SavedStateAndModListDo"));
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
            if (!await ShowConfirmAsync(T("CombinationBundles.ExportSavedPresetStateNotCurrent"),
                T("CombinationBundles.ExportBundle"))) return;
            cancellation.Token.ThrowIfCancellationRequested();
            string? folder = await PickFolderAsync();
            cancellation.Token.ThrowIfCancellationRequested();
            if (folder is null || _onlineWindowClosed) return;
            string output = Path.Combine(folder, $"Combination-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}.imm-preset.zip");
            var progress = BundleProgress(cancellation.Token, T("CombinationBundles.ExportingBundle"));
            await Task.Run(() => CombinationBundleService.Export(root, output, name, game, includeState, mods,
                cancellation.Token, progress: progress));
            await ReportOnlineActionErrorAsync(T("CombinationBundles.BundleCreatedModsSavedValues", mods.Length, mods.Sum(mod => mod.Values.Count), output), T("CombinationBundles.ExportComplete"));
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
            var validationProgress = BundleProgress(cancellation.Token, T("CombinationBundles.ValidatingBundle"));
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
                throw new IOException(T("CombinationBundles.RepositoryChangedSelectTheBundleAgain"));
            var progress = BundleProgress(cancellation.Token, T("CombinationBundles.InstallingBundle"));
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
                    await ReportOnlineActionErrorAsync(string.Join("\n", import.CleanupWarnings), T("CombinationBundles.SomeFilesNeedInspection"));
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
        string summary = T("CombinationBundles.ModsValues", manifest.Name, manifest.Mods.Count, manifest.Mods.Sum(mod => mod.Values.Count));
        summary += T("CombinationBundles.InstallIntoTheCurrentRepositoryAnd");
        if (manifest.Game.Length > 0) summary += "\n" + T("CombinationBundles.BundleGame") + manifest.Game;
        if (manifest.Game.Length > 0 && currentGame.Length > 0 && !manifest.Game.Equals(currentGame, StringComparison.OrdinalIgnoreCase))
            summary += T("CombinationBundles.WarningGameLabelDiffersFromThis");
        if (manifest.Mods.Count == 0) summary += T("CombinationBundles.EmptySetRestoringItRemovesRecognized");
        panel.Children.Add(new TextBlock { Text = summary, TextWrapping = TextWrapping.Wrap });
        var list = new ListView
        {
            SelectionMode = ListViewSelectionMode.None, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            ItemsSource = import.Preview.Select(item => new CombinationPreviewRow(item.RelativePath, "", item.Disposition switch
            {
                CombinationBundleDisposition.Install => T("CombinationBundles.NewInstall"),
                CombinationBundleDisposition.Reuse => T("CombinationBundles.IdenticalFilesReuse"),
                _ => T("CombinationBundles.PathContentConflictBlocked")
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
        var restore = new CheckBox { Content = T("CombinationBundles.ReviewDeploymentAndStateRestoreAfter"),
            IsChecked = true, IsEnabled = import.CanInstall };
        Grid.SetRow(restore, 2); panel.Children.Add(restore);
        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot, RequestedTheme = RootGrid.ActualTheme,
            Title = T("CombinationBundles.BundleInstallationPreview"), Content = panel,
            PrimaryButtonText = T("CombinationBundles.InstallAndSavePreset"), CloseButtonText = T("Common.Cancel"),
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
            T("CombinationBundles.CombinationBundle"));
    }
}
