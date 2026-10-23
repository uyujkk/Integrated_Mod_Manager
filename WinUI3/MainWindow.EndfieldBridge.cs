using System.ComponentModel;
using System.Diagnostics;
using IntegratedModManager.Core.Endfield;
using IntegratedModManager.Core.Layout;
using IntegratedModManager.EndfieldLaunch;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

// Endfield launch settings and the integrated launch. The launch itself is the AGPL-3.0-only
// IntegratedModManager.EndfieldLaunch component; this page only edits its settings and starts it.
public sealed partial class MainWindow
{
    private const string EndfieldLaunchSourceUrl = "https://github.com/uyujkk/Integrated_Mod_Manager";
    private int _endfieldBridgeOfferRevision;
    private bool _bridgeOperationInProgress;
    private bool _endfieldLaunchInProgress;

    // Manager 4.1.1–4.2.0 downloaded the standalone bridge here; read only to migrate its settings.
    private string LegacyBridgeToolsRoot => Path.Combine(AppContext.BaseDirectory, "cache", "optional-tools");
    private static string LaunchExecutablePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "ModFolderCopier.WinUI.exe");
    private static bool CanUseEndfieldBridge(WorkspaceRepository? repository) => repository is not null
        && EndfieldBridgeIntegration.IsEndfield(repository.OnlineCategoryId, repository.OnlineGameName);

    private EndfieldLaunchService CreateEndfieldLaunchService() => new(Program.EndfieldLaunchDataDirectory, LaunchExecutablePath);

    private string BridgeRiskText => T("EndfieldBridge.AccountRiskTheIntegratedEndfieldLaunch");

    private IEnumerable<string> DefaultLoaderPaths()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Path.Combine(appData, "XXMI Launcher", "Resources", "Bin", "XXMI Launcher.exe");
        yield return Path.Combine(appData, "XXMI Launcher", "XXMI Launcher.exe");
        yield return Path.Combine(programFiles, "XXMI Launcher", "Resources", "Bin", "XXMI Launcher.exe");
    }

    private void RefreshEndfieldBridgeControls()
    {
        if (BridgeOptionsButton is null) return;
        WorkspaceRepository? repository = GetSelectedRepository();
        BridgeOptionsButton.Visibility = CanUseEndfieldBridge(repository) ? Visibility.Visible : Visibility.Collapsed;
        string label = repository?.UseEndfieldBridge == true
            ? T("EndfieldBridge.LaunchIntegratedEndfieldLaunchSettingsRisks")
            : T("EndfieldBridge.LaunchExistingLoaderIntegratedLaunchAvailable");
        BridgeOptionsButton.Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap };
        BridgeOptionsButton.IsEnabled = !_bridgeOperationInProgress && !_endfieldLaunchInProgress;
        ToolTipService.SetToolTip(BridgeOptionsButton, T("EndfieldBridge.AppliesOnlyToThisEndfieldRepository"));
    }

    private async Task OfferEndfieldBridgeAfterUpgradeAsync()
    {
        // Isolated demo fixtures must never offer to configure or launch a real game.
        WorkspaceRepository? repository = _repositories.FirstOrDefault(item => CanUseEndfieldBridge(item)
            && item.Id != "state-slots-offline-demo");
        if (!EndfieldBridgeIntegration.ShouldOffer(repository is not null, _endfieldBridgeOfferRevision)) return;
        await ShowEndfieldBridgeOptionsAsync(repository!, isUpgradeOffer: true);
    }

    private async void OnBridgeOptionsClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveSelectedRepositoryFromInputs();
            if (GetSelectedRepository() is { } repository && CanUseEndfieldBridge(repository))
                await ShowEndfieldBridgeOptionsAsync(repository, isUpgradeOffer: false);
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnBridgeOptionsClicked", ex);
        }
    }

    /// <summary>Fills empty Endfield settings from a standalone bridge 0.3.x and adopts its EFMI restore point.</summary>
    private bool ImportLegacyBridgeSettings(WorkspaceRepository repository, EndfieldLaunchService service)
    {
        if (!EndfieldBridgeIntegration.HasLegacyInstallation(LegacyBridgeToolsRoot)) return false;
        string legacy = EndfieldBridgeIntegration.LegacyInstallDirectory(LegacyBridgeToolsRoot);
        bool changed = false;
        try
        {
            changed |= service.AdoptLegacyRestoreState(legacy);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EndfieldLaunchException or System.Text.Json.JsonException)
        {
            LogApplicationIssue("Adopt legacy bridge restore point", ex);
        }

        if (!string.IsNullOrWhiteSpace(repository.EndfieldGamePath) || EndfieldLaunchService.ReadLegacyProfile(legacy) is not { } profile)
        {
            return changed;
        }

        repository.EndfieldGamePath = profile.Game;
        repository.EndfieldParentPath = profile.Parent;
        repository.EndfieldGameArguments = profile.GameArguments;
        repository.EndfieldUseMods = profile.WithMods;
        if (string.IsNullOrWhiteSpace(repository.LauncherPath) && !string.IsNullOrWhiteSpace(profile.Xxmi)) repository.LauncherPath = profile.Xxmi;
        // The risk was already accepted explicitly when the standalone bridge was enabled.
        if (repository.UseEndfieldBridge) repository.EndfieldRiskAccepted = true;
        return true;
    }

    private LaunchProfile BuildLaunchProfile(WorkspaceRepository repository, string? sessionId = null) => new()
    {
        Game = repository.EndfieldGamePath.Trim(),
        Parent = repository.EndfieldParentPath.Trim(),
        Xxmi = EndfieldBridgeIntegration.ResolveLoader(repository.LauncherPath, DefaultLoaderPaths()),
        GameArguments = repository.EndfieldGameArguments.Trim(),
        WithMods = repository.EndfieldUseMods,
        Language = _currentLanguage == AppLanguage.EnUs ? "en-US" : "zh-CN",
        SessionId = sessionId ?? string.Empty
    };

    private async Task ShowEndfieldBridgeOptionsAsync(WorkspaceRepository repository, bool isUpgradeOffer)
    {
        if (_bridgeOperationInProgress) return;
        _bridgeOperationInProgress = true;
        RefreshEndfieldBridgeControls();
        try
        {
            EndfieldLaunchService service = CreateEndfieldLaunchService();
            bool migrated = ImportLegacyBridgeSettings(repository, service);
            if (migrated) SaveShellConfig();
            bool english = _currentLanguage == AppLanguage.EnUs;
            DialogViewport viewport = DialogViewportPolicy.Selection(RootGrid.ActualWidth, RootGrid.ActualHeight);

            var gameBox = PathBox(repository.EndfieldGamePath, T("EndfieldBridge.SelectEndfieldExeInTheGame"));
            var parentBox = PathBox(repository.EndfieldParentPath, T("EndfieldBridge.LeaveEmptyToFindAndVerify"));
            var loaderBox = PathBox(EndfieldBridgeIntegration.ResolveLoader(repository.LauncherPath, DefaultLoaderPaths()), T("EndfieldBridge.XXMILauncherExe"));
            var argumentsBox = PathBox(repository.EndfieldGameArguments, "-force-d3d11");
            // Switch captions stay short (ToggleSwitch content cannot wrap); the explanation wraps below it.
            var modsSwitch = new ToggleSwitch
            {
                Header = T("EndfieldBridge.LoadModsThroughXXMIEFMI"),
                OnContent = T("Common.On"), OffContent = T("Common.Off"),
                IsOn = repository.EndfieldUseMods
            };
            var modsHint = HintText();
            var integrated = new ToggleSwitch
            {
                Header = T("EndfieldBridge.LaunchMethod"),
                OnContent = T("EndfieldBridge.IntegratedEndfieldLaunch"),
                OffContent = T("EndfieldBridge.ExistingLoader"),
                IsOn = repository.UseEndfieldBridge
            };
            var launchHint = HintText();
            var output = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"), FontSize = 12 };
            // Results sit below the scrolling settings, so showing them never pushes the form around.
            var outputViewer = new ScrollViewer
            {
                Content = output, MaxHeight = 140,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Disabled
            };
            var outputClose = new Button
            {
                Content = new FontIcon { Glyph = "", FontSize = 12 }, Padding = new Thickness(8, 4, 8, 4),
                Style = (Style)Application.Current.Resources["SecondaryButtonStyle"], VerticalAlignment = VerticalAlignment.Top
            };
            ToolTipService.SetToolTip(outputClose, T("EndfieldBridge.CloseResult"));
            var outputGrid = new Grid { ColumnSpacing = 8, RowSpacing = 6 };
            outputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            outputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            outputGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            outputGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            outputGrid.Children.Add(new TextBlock { Text = T("EndfieldBridge.Result"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(outputClose, 1);
            outputGrid.Children.Add(outputClose);
            Grid.SetRow(outputViewer, 1);
            Grid.SetColumnSpan(outputViewer, 2);
            outputGrid.Children.Add(outputViewer);
            var outputBorder = new Border { Style = (Style)Application.Current.Resources["InsetBorderStyle"], Padding = new Thickness(12, 8, 8, 10), Child = outputGrid, Visibility = Visibility.Collapsed };
            outputClose.Click += (_, _) => outputBorder.Visibility = Visibility.Collapsed;
            var consent = new CheckBox
            {
                Content = new TextBlock { Text = T("EndfieldBridge.IUnderstandTheAdditionalAccountRisk"), TextWrapping = TextWrapping.Wrap },
                IsChecked = repository.EndfieldRiskAccepted
            };

            LaunchProfile CurrentProfile() => new()
            {
                Game = gameBox.Text.Trim(), Parent = parentBox.Text.Trim(), Xxmi = loaderBox.Text.Trim(),
                GameArguments = argumentsBox.Text.Trim(), WithMods = modsSwitch.IsOn,
                Language = english ? "en-US" : "zh-CN"
            };
            void Show(LaunchText text, bool failure = false)
            {
                output.Text = (failure ? T("EndfieldBridge.NotCompleted") : string.Empty) + text.For(english);
                outputBorder.Visibility = Visibility.Visible;
            }

            Button ActionButton(string text, Func<Task> action)
            {
                var button = new Button
                {
                    // Wrapping label: long English labels must not be clipped in narrow columns.
                    Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center },
                    Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
                    HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch
                };
                button.Click += async (_, _) =>
                {
                    button.IsEnabled = false;
                    try
                    {
                        await action();
                    }
                    catch (EndfieldLaunchException ex)
                    {
                        Show(ex.Text, failure: true);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException
                        or InvalidOperationException or ArgumentException)
                    {
                        Show(LaunchText.Both(ex.Message), failure: true);
                    }
                    catch (Exception ex)
                    {
                        ReportUiActionFailure("Endfield launch settings: " + text, ex);
                    }
                    finally
                    {
                        button.IsEnabled = true;
                    }
                };
                return button;
            }

            // ContentDialog sizes to its content, so a width is needed for the larger max width to apply.
            var body = new StackPanel { Spacing = 14, Width = Math.Max(320, Math.Min(760, viewport.MaxWidth - 72)) };
            body.Children.Add(new TextBlock
            {
                Text = isUpgradeOffer
                    ? L($"仓库：{repository.Name}\n终末地启动已内置到管理器，不再需要单独下载 EndfieldBridge。" + (migrated ? "已导入旧桥接器的设置；如果之前适配过 EFMI，请点击“备份并适配 EFMI”切换到内置命令，原始恢复点会保留。" : string.Empty),
                        $"Repository: {repository.Name}\nThe Endfield launch is now built into the manager; EndfieldBridge no longer needs a separate download." + (migrated ? " Settings from the old bridge were imported. If EFMI was set up before, use Back up and set up EFMI to switch to the integrated command; the original restore point is kept." : string.Empty))
                    : T("EndfieldBridge.Repository", repository.Name),
                TextWrapping = TextWrapping.Wrap,
                Style = (Style)Application.Current.Resources["CaptionTextStyle"]
            });
            body.Children.Add(new InfoBar
            {
                Severity = InfoBarSeverity.Warning, IsOpen = true, IsClosable = false,
                Title = T("EndfieldBridge.AccountRisk"), Message = BridgeRiskText
            });

            // The risk confirmation gates Save, so it sits right under the switch it belongs to.
            var launch = new StackPanel { Spacing = 6 };
            launch.Children.Add(integrated);
            launch.Children.Add(launchHint);
            launch.Children.Add(consent);
            body.Children.Add(launch);

            var paths = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
            paths.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            paths.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            paths.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            AddPathRow(paths, 0, T("EndfieldBridge.GameExecutable"), gameBox, ".exe");
            AddPathRow(paths, 1, T("EndfieldBridge.OfficialLauncherOptional"), parentBox, ".exe");
            AddPathRow(paths, 2, T("EndfieldBridge.XXMILauncher"), loaderBox, ".exe");
            AddPathRow(paths, 3, T("EndfieldBridge.GameArguments"), argumentsBox, null);
            var settings = new StackPanel { Spacing = 10 };
            settings.Children.Add(SectionTitle(T("EndfieldBridge.GameAndLoader")));
            settings.Children.Add(paths);
            settings.Children.Add(modsSwitch);
            settings.Children.Add(modsHint);
            body.Children.Add(new Border { Style = (Style)Application.Current.Resources["InsetBorderStyle"], Padding = new Thickness(14, 12, 14, 14), Child = settings });

            var actions = new StackPanel { Spacing = 8 };
            actions.Children.Add(SectionTitle(T("EndfieldBridge.ChecksAndEFMISetup")));
            var actionRow = new ButtonGrid(3);
            actionRow.Add(ActionButton(T("EndfieldBridge.Check"), async () =>
            {
                LaunchProfile profile = CurrentProfile();
                Show(await Task.Run(() => service.Check(profile)));
            }));
            actionRow.Add(ActionButton(T("EndfieldBridge.ImportFromEFMI"), () =>
            {
                LaunchProfile profile = CurrentProfile();
                LaunchText note = service.Integration.Import(profile);
                gameBox.Text = profile.Game;
                argumentsBox.Text = profile.GameArguments;
                Show(note + new LaunchText("\n", "\n") + service.Integration.Inspect(profile));
                return Task.CompletedTask;
            }));
            actionRow.Add(ActionButton(T("EndfieldBridge.BackUpAndSetUpEFMI"), () =>
            {
                LaunchProfile profile = CurrentProfile();
                service.Integration.Import(profile);
                gameBox.Text = profile.Game;
                argumentsBox.Text = profile.GameArguments;
                if (!integrated.IsOn || consent.IsChecked != true)
                {
                    Show(new LaunchText("请先开启内置启动并勾选账号风险确认，再适配 EFMI。", "Enable integrated launch and confirm the account risk before setting up EFMI."), failure: true);
                    return Task.CompletedTask;
                }

                string? backup = service.ConfigureXxmi(profile);
                modsSwitch.IsOn = true;
                if (backup is null)
                {
                    Show(new LaunchText("适配已完成，启动配置已同步，原始恢复点未覆盖。\n", "Setup is complete; the launch profile was saved and the original restore point kept.\n") + service.Integration.AuthorizationStatus(profile));
                    return Task.CompletedTask;
                }

                Show(new LaunchText($"已适配 EFMI。完整备份：{backup}\n首次启动时 XXMI 会提示命令未验证：核对命令指向本管理器后选择“保持”。\n",
                    $"EFMI is set up. Full backup: {backup}\nOn first launch XXMI reports an unverified command: check that it points to this manager, then choose Keep.\n") + service.Integration.Inspect(profile));
                return Task.CompletedTask;
            }));
            actionRow.Add(ActionButton(T("EndfieldBridge.RestoreEFMI"), () =>
            {
                service.Integration.Restore();
                modsSwitch.IsOn = false;
                Show(new LaunchText("已恢复适配前的 EFMI 启动字段，其他设置保留。", "EFMI launch fields were restored to their values before setup; other settings were kept."));
                return Task.CompletedTask;
            }));
            actionRow.Add(ActionButton(T("EndfieldBridge.OpenLaunchLog"), () =>
            {
                if (!File.Exists(service.Store.LogPath)) service.Store.Log(new LaunchText("日志已创建。", "Log created."), english);
                OpenTextDocument(service.Store.LogPath);
                return Task.CompletedTask;
            }));
            actions.Children.Add(actionRow.Panel);
            body.Children.Add(new Border { Style = (Style)Application.Current.Resources["InsetBorderStyle"], Padding = new Thickness(14, 12, 14, 14), Child = actions });

            var legal = new StackPanel { Spacing = 8 };
            legal.Children.Add(new TextBlock
            {
                Text = T("EndfieldBridge.TheIntegratedLaunchComponentDerivesFrom"),
                TextWrapping = TextWrapping.Wrap,
                Style = (Style)Application.Current.Resources["CaptionTextStyle"]
            });
            var legalRow = new ButtonGrid(3);
            legalRow.Add(ActionButton(T("EndfieldBridge.AGPL30License"), () => { OpenTextDocument(Path.Combine(AppContext.BaseDirectory, "licenses", "AGPL-3.0.txt")); return Task.CompletedTask; }));
            legalRow.Add(ActionButton(T("EndfieldBridge.CreditsAndChanges"), () => { OpenTextDocument(Path.Combine(AppContext.BaseDirectory, "licenses", "EndfieldLaunch-NOTICE.md")); return Task.CompletedTask; }));
            legalRow.Add(ActionButton(T("EndfieldBridge.CorrespondingSource"), () => OpenExternalUrlAsync(EndfieldLaunchSourceUrl, T("EndfieldBridge.FailedToOpenPage"))));
            legal.Children.Add(legalRow.Panel);
            body.Children.Add(new Expander
            {
                Header = T("EndfieldBridge.LicenseAndSource"),
                Content = legal,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            });
            // A fixed height (not MaxHeight) bounds the inner ScrollViewer: otherwise the dialog clips the
            // lower sections without a scrollbar. Results get their own row under the settings.
            var content = new Grid { Height = viewport.ContentHeight, RowSpacing = 10 };
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var settingsViewer = new ScrollViewer
            {
                Content = body,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollMode = ScrollMode.Disabled,
                Padding = new Thickness(0, 0, 14, 0)
            };
            content.Children.Add(settingsViewer);
            Grid.SetRow(outputBorder, 1);
            content.Children.Add(outputBorder);
            var dialog = new ContentDialog
            {
                Title = T("EndfieldBridge.EndfieldLaunchSettings"),
                Content = content,
                PrimaryButtonText = T("Common.Save"),
                CloseButtonText = T("Common.Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                RequestedTheme = RootGrid.ActualTheme,
                XamlRoot = RootGrid.XamlRoot
            };
            dialog.Resources["ContentDialogMaxWidth"] = Math.Min(860, viewport.MaxWidth);
            void RefreshState()
            {
                bool useIntegrated = integrated.IsOn;
                consent.Visibility = useIntegrated ? Visibility.Visible : Visibility.Collapsed;
                dialog.IsPrimaryButtonEnabled = !useIntegrated || consent.IsChecked == true;
                launchHint.Text = useIntegrated
                    ? T("EndfieldBridge.StartsTheGameUnderTheOfficially")
                    : T("EndfieldBridge.RunsTheXXMIOrOtherLauncher");
                modsHint.Text = modsSwitch.IsOn
                    ? T("EndfieldBridge.LoadsModsThroughXXMIEFMISet")
                    : T("EndfieldBridge.StartsTheGameDirectlyWithoutMods");
            }

            integrated.Toggled += (_, _) => RefreshState();
            modsSwitch.Toggled += (_, _) => RefreshState();
            consent.Checked += (_, _) => RefreshState();
            consent.Unchecked += (_, _) => RefreshState();
            RefreshState();

            dialog.PrimaryButtonClick += (_, args) =>
            {
                if (!integrated.IsOn) return;
                try
                {
                    service.SaveSettings(CurrentProfile());
                }
                catch (Exception ex)
                {
                    args.Cancel = true;
                    Show(ex is EndfieldLaunchException known ? known.Text : LaunchText.Both(ex.Message), failure: true);
                }
            };

            ContentDialogResult result = await dialog.ShowAsync();
            _endfieldBridgeOfferRevision = EndfieldBridgeIntegration.OfferRevision;
            if (result == ContentDialogResult.Primary)
            {
                repository.UseEndfieldBridge = integrated.IsOn;
                if (repository.UseEndfieldBridge) repository.EndfieldRiskAccepted = true;
                repository.EndfieldGamePath = gameBox.Text.Trim();
                repository.EndfieldParentPath = parentBox.Text.Trim();
                repository.EndfieldGameArguments = argumentsBox.Text.Trim();
                repository.EndfieldUseMods = modsSwitch.IsOn;
                repository.LauncherPath = loaderBox.Text.Trim();
                if (GetSelectedRepository()?.Id == repository.Id) LauncherTextBox.Text = repository.LauncherPath;
                StatusTextBlock.Text = repository.UseEndfieldBridge
                    ? T("EndfieldBridge.EndfieldLaunchSettingsSavedRunLauncher")
                    : T("EndfieldBridge.SavedRunLauncherStartsTheExisting");
            }

            SaveShellConfig();
        }
        catch (Exception ex)
        {
            if (_onlineWindowClosed) return;
            LogApplicationIssue("Endfield launch settings", ex);
            await ShowMessageAsync(T("EndfieldBridge.CouldNotOpenEndfieldLaunchSettings") + ex.Message, T("EndfieldBridge.EndfieldLaunch"));
        }
        finally
        {
            _bridgeOperationInProgress = false;
            if (!_onlineWindowClosed)
            {
                RefreshEndfieldBridgeControls();
                RefreshDashboard();
            }
        }

        TextBox PathBox(string text, string placeholder) => new() { Text = text, PlaceholderText = placeholder, MinWidth = 220 };

        TextBlock SectionTitle(string text) => new() { Text = text, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };

        TextBlock HintText() => new() { TextWrapping = TextWrapping.Wrap, Opacity = .75, FontSize = 12 };

        void AddPathRow(Grid grid, int row, string label, TextBox box, string? extension)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            // Wrap long labels instead of letting an auto-width column squeeze the path field.
            var caption = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 150 };
            // Same inset field as the main window's path boxes; the app's TextBox style is borderless.
            box.BorderThickness = new Thickness(0);
            box.Padding = new Thickness(12, 8, 12, 8);
            box.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            var field = new Border { Style = (Style)Application.Current.Resources["InsetBorderStyle"], Child = box };
            Grid.SetRow(caption, row);
            Grid.SetRow(field, row);
            Grid.SetColumn(field, 1);
            grid.Children.Add(caption);
            grid.Children.Add(field);
            if (extension is null)
            {
                Grid.SetColumnSpan(field, 2);
                return;
            }

            var browse = new Button { Content = T("EndfieldBridge.Browse"), Style = (Style)Application.Current.Resources["SecondaryButtonStyle"] };
            browse.Click += async (_, _) => await RunUiActionAsync("Pick Endfield path", async () =>
            {
                string? picked = await PickFileAsync([extension]);
                if (!string.IsNullOrEmpty(picked)) box.Text = picked;
            });
            Grid.SetRow(browse, row);
            Grid.SetColumn(browse, 2);
            grid.Children.Add(browse);
        }

    }

    private void OpenTextDocument(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException(T("EndfieldBridge.FileNotFound") + path);
        Process.Start(new ProcessStartInfo("notepad.exe", CommandLine.Quote(path)) { UseShellExecute = true });
    }

    private async Task RunRepositoryLauncherAsync()
    {
        if (_bridgeOperationInProgress || _endfieldLaunchInProgress) return;
        WorkspaceRepository? repository = GetSelectedRepository();
        bool useIntegrated = CanUseEndfieldBridge(repository) && repository!.UseEndfieldBridge;
        try
        {
            if (useIntegrated)
            {
                SaveSelectedRepositoryFromInputs();
                await StartIntegratedEndfieldLaunchAsync(repository!);
                return;
            }

            string loader = CanUseEndfieldBridge(repository)
                ? EndfieldBridgeIntegration.ResolveLoader(LauncherTextBox.Text, DefaultLoaderPaths())
                : (LauncherTextBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(loader) || !File.Exists(loader))
            {
                await ShowMessageAsync(T("EndfieldBridge.NoValidDefaultXXMIPathWas"), T("EndfieldBridge.LauncherNotSet"));
                return;
            }

            Process.Start(new ProcessStartInfo(loader)
            {
                WorkingDirectory = Path.GetDirectoryName(loader) ?? AppContext.BaseDirectory,
                UseShellExecute = true
            });
            StatusTextBlock.Text = T("EndfieldBridge.ExistingLoaderStarted");
            if (string.IsNullOrWhiteSpace(LauncherTextBox.Text)) LauncherTextBox.Text = loader;
            SaveConfig();
        }
        catch (Exception ex)
        {
            await ShowMessageAsync(T("EndfieldBridge.FailedToRunLauncher") + ex.Message, T("EndfieldBridge.LaunchFailed"));
        }
    }

    private async Task StartIntegratedEndfieldLaunchAsync(WorkspaceRepository repository)
    {
        EndfieldLaunchService service = CreateEndfieldLaunchService();
        if (ImportLegacyBridgeSettings(repository, service)) SaveShellConfig();
        if (!repository.EndfieldRiskAccepted || !EndfieldGame.IsValidGame(repository.EndfieldGamePath))
        {
            await ShowEndfieldBridgeOptionsAsync(repository, isUpgradeOffer: false);
            return;
        }

        if (LaunchCommand.IsLaunchRunning())
        {
            await ShowMessageAsync(T("EndfieldBridge.AnEndfieldLaunchSessionIsAlready"), T("EndfieldBridge.EndfieldLaunch"));
            return;
        }

        string sessionId = Guid.NewGuid().ToString("N");
        LaunchProfile profile = BuildLaunchProfile(repository, sessionId);
        if (profile.WithMods && !File.Exists(profile.Xxmi))
        {
            await ShowMessageAsync(T("EndfieldBridge.ModLoadingIsOnButXXMI"), T("EndfieldBridge.EndfieldLaunch"));
            return;
        }

        service.Store.SaveProfile(profile);
        try
        {
            // One UAC prompt: the manager's own executable runs the launch without a window.
            Process.Start(new ProcessStartInfo(LaunchExecutablePath, LaunchCommand.LaunchVerb)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            })?.Dispose();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            StatusTextBlock.Text = T("EndfieldBridge.AdministratorApprovalWasDeclinedTheGame");
            return;
        }

        _endfieldLaunchInProgress = true;
        RefreshEndfieldBridgeControls();
        StatusTextBlock.Text = T("EndfieldBridge.StartingEndfield");
        try
        {
            LaunchStatus? status = await WaitForLaunchStatusAsync(service.Store, sessionId, TimeSpan.FromSeconds(profile.WithMods ? 120 : 45));
            bool english = profile.English;
            switch (status?.State)
            {
                case LaunchStates.GameStarted:
                    StatusTextBlock.Text = status!.Message.For(english);
                    ShowAppNotification("终末地已启动。启动成功不代表账号安全。", "Endfield started. A successful launch does not establish account safety.");
                    break;
                case LaunchStates.Failed:
                case LaunchStates.Exited:
                    StatusTextBlock.Text = T("EndfieldBridge.TheEndfieldLaunchDidNotComplete");
                    await ShowMessageAsync(status!.Message.For(english), T("EndfieldBridge.EndfieldLaunch"));
                    break;
                case LaunchStates.LoaderStarted:
                    StatusTextBlock.Text = T("EndfieldBridge.HandedToXXMIEFMINoGame");
                    break;
                default:
                    StatusTextBlock.Text = T("EndfieldBridge.NoLaunchResultYetOpenThe");
                    break;
            }
        }
        finally
        {
            _endfieldLaunchInProgress = false;
            if (!_onlineWindowClosed) RefreshEndfieldBridgeControls();
        }
    }

    private async Task<LaunchStatus?> WaitForLaunchStatusAsync(LaunchStore store, string sessionId, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        LaunchStatus? latest = null;
        while (stopwatch.Elapsed < timeout && !_onlineWindowClosed)
        {
            await Task.Delay(300);
            LaunchStatus? status = store.ReadStatus();
            if (status is null || status.SessionId != sessionId) continue;
            latest = status;
            if (LaunchStates.IsTerminal(status.State)) break;
        }

        return latest;
    }

    // Equal-width command buttons in rows, so long labels in either language never clip.
    private sealed class ButtonGrid
    {
        private readonly Grid _grid = new() { ColumnSpacing = 8, RowSpacing = 8 };
        private readonly int _columns;
        private int _count;

        public ButtonGrid(int columns)
        {
            _columns = columns;
            for (int i = 0; i < columns; i++) _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        public Panel Panel => _grid;

        public void Add(Button button)
        {
            if (_count % _columns == 0) _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            Grid.SetRow(button, _count / _columns);
            Grid.SetColumn(button, _count % _columns);
            _grid.Children.Add(button);
            _count++;
        }
    }
}
