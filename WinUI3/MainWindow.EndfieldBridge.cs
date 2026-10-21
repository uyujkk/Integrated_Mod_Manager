using System.Diagnostics;
using System.Text.Json;
using IntegratedModManager.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private int _endfieldBridgeOfferRevision;
    private bool _bridgeOperationInProgress;
    private CancellationTokenSource? _bridgeDownloadCancellation;
    // This user-data directory is excluded from release packages and preserved by the updater.
    private string BridgeToolsRoot => Path.Combine(AppContext.BaseDirectory, "cache", "optional-tools");
    private static bool CanUseEndfieldBridge(WorkspaceRepository? repository) => repository is not null
        && EndfieldBridgeIntegration.IsEndfield(repository.OnlineCategoryId, repository.OnlineGameName);

    private string BridgeRiskText => L(
        "账号风险：EndfieldBridge 是非官方第三方工具，会改变正常启动链；结合 XXMI / EFMI 使用 Mod 可能导致账号处罚或封禁。签名、校验和与启动成功均不代表官方认可或账号安全。\n\n不下载则使用原有加载器；加载器加载第三方 Mod 也不等于零风险。不能接受账号风险时，请使用官方启动器且不加载第三方 Mod。",
        "Account risk: EndfieldBridge is an unofficial third-party tool that changes the normal launch chain. Using Mods with XXMI / EFMI may lead to account penalties or bans. Signatures, checksums and successful launches do not establish official approval or account safety.\n\nDeclining keeps the existing loader. Loading third-party Mods through that loader is not risk-free either. If you cannot accept account risk, use the official launcher without third-party Mods.");

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
            ? L("启动方式：EndfieldBridge · 管理与风险", "Launch: EndfieldBridge · Manage / Risks")
            : L("启动方式：原有加载器 · 可选桥接器", "Launch: Existing loader · Optional bridge");
        BridgeOptionsButton.Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap };
        BridgeOptionsButton.IsEnabled = !_bridgeOperationInProgress;
        ToolTipService.SetToolTip(BridgeOptionsButton, L("仅作用于当前终末地仓库，不会自动启动游戏。", "Applies only to this Endfield repository. Does not auto-launch the game."));
    }

    private async Task OfferEndfieldBridgeAfterUpgradeAsync()
    {
        // Isolated demo fixtures must never offer to install or launch a real tool.
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

    private async Task ShowEndfieldBridgeOptionsAsync(WorkspaceRepository repository, bool isUpgradeOffer)
    {
        if (_bridgeOperationInProgress) return;
        _bridgeOperationInProgress = true;
        RefreshEndfieldBridgeControls();
        try
        {
            bool installed = EndfieldBridgeIntegration.IsInstalled(BridgeToolsRoot);
            var body = new StackPanel { Spacing = 12 };
            body.Children.Add(new TextBlock
            {
                Text = L($"仓库：{repository.Name}\n可选独立组件 EndfieldBridge {EndfieldBridgeIntegration.Version}，下载约 41 MB。",
                    $"Repository: {repository.Name}\nOptional standalone component EndfieldBridge {EndfieldBridgeIntegration.Version}, approximately 41 MB."),
                TextWrapping = TextWrapping.Wrap
            });
            string loader = EndfieldBridgeIntegration.ResolveLoader(repository.LauncherPath, DefaultLoaderPaths());
            body.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(loader)
                    ? L("未找到默认 XXMI 安装位置；不下载桥接器时，请在启动器路径中选择实际的 XXMI Launcher.exe。", "Default XXMI installation not found. If you decline the bridge, select your actual XXMI Launcher.exe in the launcher path.")
                    : L("原有加载器：", "Existing loader: ") + loader,
                TextWrapping = TextWrapping.Wrap
            });
            body.Children.Add(new TextBlock { Text = BridgeRiskText, TextWrapping = TextWrapping.Wrap });
            body.Children.Add(new TextBlock
            {
                Text = L("下载来源：uyujkk/EndfieldBridge 的固定版本 GitHub Release；下载后进行 SHA-256 校验。桥接器为 AGPL-3.0-only 独立程序，保留许可证、来源与对应源码入口，管理器许可证不变。",
                    "Source: a pinned release from uyujkk/EndfieldBridge on GitHub, verified with SHA-256. The bridge remains a separate AGPL-3.0-only program with its license, notices and corresponding source link. The manager license is unchanged."),
                TextWrapping = TextWrapping.Wrap,
                Style = (Style)Application.Current.Resources["CaptionTextStyle"]
            });
            body.Children.Add(new TextBlock
            {
                Text = L("首次使用先在桥接器中配置游戏和加载器。管理器不自动修改 EFMI 或确认 XXMI 授权。关闭桥接选项只切换管理器入口：如果此前适配过 EFMI，请先在桥接器内恢复 EFMI，才能恢复原有启动链。",
                    "Configure your game and loader in the bridge first. The manager does not change EFMI or approve XXMI commands. Switching off only changes the manager entry: restore EFMI inside the bridge first if you previously adapted it, to restore the original launch chain."),
                TextWrapping = TextWrapping.Wrap
            });
            var links = new StackPanel { Spacing = 6 };
            var release = new Button { Content = L("查看发布、风险和对应源码", "Release, risks and corresponding source") };
            release.Click += async (_, _) => await RunUiActionAsync("OpenExternalUrlAsync", () => OpenExternalUrlAsync(EndfieldBridgeIntegration.ReleaseUrl, L("打开页面失败", "Failed to open page")));
            links.Children.Add(release);
            if (installed)
            {
                var configure = new Button { Content = L("打开桥接器配置（不启动游戏）", "Configure bridge (does not launch game)") };
                configure.Click += (_, _) =>
                {
                    try { OpenBridgeConfiguration(); }
                    catch (Exception ex) { configure.Content = L("打开失败：", "Open failed: ") + ex.Message; }
                };
                links.Children.Add(configure);
            }
            body.Children.Add(links);
            var consent = new CheckBox
            {
                Content = new TextBlock { Text = L("我已了解额外风险，选择为此仓库启用桥接器。", "I understand the additional risks and choose to enable the bridge for this repository."), TextWrapping = TextWrapping.Wrap },
                IsChecked = false
            };
            body.Children.Add(consent);
            var dialog = new ContentDialog
            {
                Title = isUpgradeOffer ? L("升级后可选：终末地启动桥接器", "Optional after upgrade: Endfield Bridge")
                    : L("终末地启动方式与风险", "Endfield launch method and risks"),
                Content = new ScrollViewer
                {
                    Content = body,
                    MaxHeight = Math.Max(180, Math.Min(520, RootGrid.ActualHeight - 220)),
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    HorizontalScrollMode = ScrollMode.Disabled
                },
                PrimaryButtonText = installed ? L("使用桥接器", "Use bridge") : L("下载并使用", "Download and use"),
                SecondaryButtonText = L("使用原有加载器", "Use existing loader"),
                CloseButtonText = L("暂不设置", "Not now"),
                IsPrimaryButtonEnabled = false,
                DefaultButton = ContentDialogButton.Close,
                RequestedTheme = RootGrid.ActualTheme,
                XamlRoot = RootGrid.XamlRoot
            };
            consent.Checked += (_, _) => dialog.IsPrimaryButtonEnabled = true;
            consent.Unchecked += (_, _) => dialog.IsPrimaryButtonEnabled = false;
            ContentDialogResult result = await dialog.ShowAsync();
            _endfieldBridgeOfferRevision = EndfieldBridgeIntegration.OfferRevision;
            SaveShellConfig();
            if (result == ContentDialogResult.Secondary)
            {
                repository.UseEndfieldBridge = false;
                repository.LauncherPath = loader;
                if (GetSelectedRepository()?.Id == repository.Id) LauncherTextBox.Text = loader;
                SaveShellConfig();
                return;
            }
            if (result != ContentDialogResult.Primary) return;
            if (!installed && !await DownloadEndfieldBridgeAsync()) return;

            // Persist only after a consented, verified installation. Never replace LauncherPath.
            repository.UseEndfieldBridge = true;
            SaveShellConfig();
            StatusTextBlock.Text = L("已选择桥接器；下次“运行启动器”将使用它。请先完成桥接配置。", "Bridge selected for Run Launcher. Complete its configuration first.");
            if (!installed)
            {
                OpenBridgeConfiguration();
                await ShowMessageAsync(L("桥接器已下载并打开配置界面，不会自动启动游戏。请选择实际游戏与 XXMI 路径，按需在桥接器中备份并适配 EFMI，核对 XXMI 的授权提示后再使用。", "The bridge was downloaded and its configuration window opened, without launching the game. Select your real game and XXMI paths, back up and set up EFMI inside the bridge if needed, and review XXMI's authorization prompt before use."),
                    L("桥接器就绪 · 需要配置", "Bridge installed · Setup required"));
            }
        }
        catch (Exception ex)
        {
            if (_onlineWindowClosed) return;
            await ShowMessageAsync(L("桥接器设置失败，原有加载器路径未被替换：", "Bridge setup failed. Your existing loader path was not replaced: ") + ex.Message,
                L("桥接器", "Endfield Bridge"));
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
    }

    private async Task<bool> DownloadEndfieldBridgeAsync()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        _bridgeDownloadCancellation = cancellation;
        var text = new TextBlock { Text = L("正在下载桥接器…", "Downloading bridge…"), TextWrapping = TextWrapping.Wrap };
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(text);
        body.Children.Add(new ProgressBar { IsIndeterminate = true });
        var dialog = new ContentDialog
        {
            Title = L("下载并校验 EndfieldBridge", "Download and verify EndfieldBridge"),
            Content = body, CloseButtonText = L("取消", "Cancel"), DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot, RequestedTheme = RootGrid.ActualTheme
        };
        dialog.CloseButtonClick += (_, _) => cancellation.Cancel();
        bool completed = false;
        dialog.Closing += (_, _) => { if (!completed) cancellation.Cancel(); };
        var presentation = dialog.ShowAsync();
        try
        {
            await Task.Run(() => EndfieldBridgeIntegration.DownloadAsync(_httpClient, BridgeToolsRoot,
                bytes => DispatcherQueue.TryEnqueue(() => text.Text = L($"已下载 {bytes / 1048576d:F1} MB；下载后将校验并安装。", $"Downloaded {bytes / 1048576d:F1} MB; verification follows.")), cancellation.Token));
            cancellation.Token.ThrowIfCancellationRequested();
            completed = true;
            return true;
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = L("桥接器下载已取消或超时，未改变启动方式。", "Bridge download canceled or timed out. Launch mode unchanged.");
            return false;
        }
        finally
        {
            _bridgeDownloadCancellation = null;
            dialog.Hide();
            await presentation;
        }
    }

    private void OpenBridgeConfiguration()
    {
        string executable = EndfieldBridgeIntegration.Executable(BridgeToolsRoot);
        Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(executable)! });
    }

    private bool IsBridgeConfiguredForLoader(string loader)
    {
        try
        {
            string path = Path.Combine(EndfieldBridgeIntegration.InstallDirectory(BridgeToolsRoot), "data", "profile.json");
            using JsonDocument profile = JsonDocument.Parse(File.ReadAllText(path));
            string game = profile.RootElement.GetProperty("Game").GetString() ?? string.Empty;
            if (!File.Exists(game) || !Path.GetFileName(game).Equals("Endfield.exe", StringComparison.OrdinalIgnoreCase)
                || !Directory.Exists(Path.Combine(Path.GetDirectoryName(game)!, "Endfield_Data"))) return false;
            if (!profile.RootElement.GetProperty("WithMods").GetBoolean()) return true;
            string configured = profile.RootElement.GetProperty("Xxmi").GetString() ?? string.Empty;
            return File.Exists(configured) && (string.IsNullOrWhiteSpace(loader)
                || Path.GetFullPath(configured).Equals(Path.GetFullPath(loader), StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    private async Task RunRepositoryLauncherAsync()
    {
        if (_bridgeOperationInProgress) return;
        WorkspaceRepository? repository = GetSelectedRepository();
        bool useBridge = CanUseEndfieldBridge(repository) && repository!.UseEndfieldBridge;
        try
        {
            string loader = CanUseEndfieldBridge(repository)
                ? EndfieldBridgeIntegration.ResolveLoader(LauncherTextBox.Text, DefaultLoaderPaths())
                : (LauncherTextBox.Text ?? string.Empty).Trim();
            string executable = loader;
            string arguments = string.Empty;
            if (useBridge)
            {
                if (!EndfieldBridgeIntegration.IsInstalled(BridgeToolsRoot))
                {
                    await ShowMessageAsync(L("所选桥接器文件缺失。请在“启动方式”中重新配置或明确切换为原有加载器；不会静默改变启动链。", "The selected bridge is missing. Reconfigure it under Launch Method or explicitly switch to the existing loader. No silent launch-chain fallback will occur."), L("桥接器不可用", "Bridge unavailable"));
                    return;
                }
                if (!IsBridgeConfiguredForLoader(loader))
                {
                    OpenBridgeConfiguration();
                    await ShowMessageAsync(L("已打开桥接器配置，没有启动游戏。请先选择有效游戏路径，并确认桥接器使用的 XXMI 与当前仓库的加载器一致。", "Bridge configuration opened without starting the game. Select a valid game and ensure the bridge uses the same XXMI loader as this repository."), L("请先配置桥接器", "Configure bridge first"));
                    return;
                }
                executable = EndfieldBridgeIntegration.Executable(BridgeToolsRoot);
                arguments = "--launch";
            }
            else if (string.IsNullOrWhiteSpace(loader) || !File.Exists(loader))
            {
                await ShowMessageAsync(L("未找到可用的默认 XXMI 路径，请选择实际的 XXMI Launcher.exe 或原有启动器。", "No valid default XXMI path was found. Select your actual XXMI Launcher.exe or existing launcher."), L("未设置启动器", "Launcher not set"));
                return;
            }
            Process.Start(new ProcessStartInfo(executable, arguments)
            {
                WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory,
                UseShellExecute = true
            });
            StatusTextBlock.Text = useBridge ? L("已调用桥接器；启动结果不代表账号安全。", "Bridge invoked. Launch success does not establish account safety.")
                : L("已启动原有加载器。", "Existing loader started.");
            if (!useBridge && string.IsNullOrWhiteSpace(LauncherTextBox.Text)) LauncherTextBox.Text = loader;
            SaveConfig();
        }
        catch (Exception ex)
        {
            await ShowMessageAsync(L("运行启动器失败：", "Failed to run launcher: ") + ex.Message, L("启动失败", "Launch failed"));
        }
    }
}
