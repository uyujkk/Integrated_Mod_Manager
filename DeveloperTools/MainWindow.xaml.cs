using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using IntegratedModManager.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace IntegratedModManager.DeveloperTools;

public sealed partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _settingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IntegratedModManagerDeveloperTools");
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly List<ArtifactItem> _artifacts = [];
    private ShellConfigSnapshot _shellConfig = new();
    private DeveloperToolSettings _settings = new();
    private string _rootPath = string.Empty;
    private string _activeFileContent = string.Empty;
    private bool _refreshing;
    private bool _applyingSettings;

    private string SettingsPath => Path.Combine(_settingsDirectory, "settings.json");
    private string DiagnosticsDirectory => Path.Combine(_settingsDirectory, "diagnostics");
    private string SnapshotsDirectory => Path.Combine(_settingsDirectory, "snapshots");
    private string ShellConfigPath => Path.Combine(_rootPath, "beta-shell.json");
    private string LegacyConfigPath => Path.Combine(_rootPath, "config.ini");
    private bool IsEnglish => _settings.English;

    public MainWindow()
    {
        InitializeComponent();
        LoadSettings();
        _rootPath = ResolveInitialRoot(_settings.RootPath);
        RootPathTextBox.Text = _rootPath;
        ApplyTheme();
        ApplyLanguage();

        _refreshTimer.Tick += async (_, _) => await RefreshAllAsync(showStatus: false);
        if (Content is FrameworkElement contentRoot)
        {
            contentRoot.Loaded += async (_, _) =>
            {
                TryApplyWindowPresentation();
                await RefreshAllAsync(showStatus: true);
            };
        }
        Closed += (_, _) => _refreshTimer.Stop();
    }

    private string L(string zh, string en) => IsEnglish ? en : zh;

    private void TryApplyWindowPresentation()
    {
        try
        {
            AppWindow.Resize(new SizeInt32(1280, 820));
            string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (File.Exists(iconPath))
            {
                AppWindow.SetIcon(iconPath);
            }
        }
        catch
        {
            // Presentation enhancements are optional.
        }
    }

    private void ApplyLanguage()
    {
        Title = L("IMM 独立开发者工具", "IMM Standalone Developer Tools");
        HeaderTitleTextBlock.Text = L("Integrated Mod Manager 开发者工具", "Integrated Mod Manager Developer Tools");
        HeaderSubtitleTextBlock.Text = L("独立运行 · 配置、仓库、日志与诊断工作台", "Standalone · configuration, repository, log, and diagnostic workbench");
        ThemeButton.Content = _settings.DarkTheme ? L("浅色", "Light") : L("深色", "Dark");
        LanguageButton.Content = IsEnglish ? "中文" : "English";
        BrowseRootButton.Content = L("选择目录", "Choose Folder");
        RefreshAllButton.Content = L("刷新全部", "Refresh All");
        AutoRefreshToggle.Header = L("自动刷新", "Auto refresh");
        AutoRefreshToggle.OnContent = L("开", "On");
        AutoRefreshToggle.OffContent = L("关", "Off");

        OverviewTab.Header = L("概览", "Overview");
        RepositoriesTab.Header = L("仓库检查", "Repositories");
        FilesTab.Header = L("配置与日志", "Files & Logs");
        DiagnosticsTab.Header = L("诊断与快照", "Diagnostics");
        VersionCardLabel.Text = L("主程序版本", "Main App Version");
        RepositoryCardLabel.Text = L("仓库数量", "Repositories");
        StorageCardLabel.Text = L("数据占用", "Data Storage");
        HealthCardLabel.Text = L("关键文件", "Critical Files");
        RuntimeTitleTextBlock.Text = L("运行与目标环境", "Runtime and Target Environment");
        StorageTitleTextBlock.Text = L("存储分布", "Storage Breakdown");

        RepositorySelectorLabel.Text = L("选择仓库", "Repository");
        ValidateRepositoryButton.Content = L("重新检查", "Validate Again");
        OpenRepositoryButton.Content = L("打开仓库", "Open Repository");
        PathInspectionTitleTextBlock.Text = L("关键路径", "Critical Paths");
        RepositoryReportTitleTextBlock.Text = L("检查报告", "Validation Report");
        ArtifactsTitleTextBlock.Text = L("已发现文件", "Discovered Files");
        FileSearchTextBox.PlaceholderText = L("在当前文件中搜索", "Search in current file");
        ValidateJsonButton.Content = L("校验 JSON", "Validate JSON");
        CopyViewerButton.Content = L("复制", "Copy");
        SaveViewerButton.Content = L("另存为", "Save As");

        DiagnosticExportTitleTextBlock.Text = L("脱敏诊断报告", "Sanitized Diagnostics");
        DiagnosticExportHintTextBlock.Text = L("打包运行概览、仓库检查、配置与日志，并替换已知的本机隐私路径。", "Package overview, validation, configuration, and logs while replacing known private local paths.");
        ExportDiagnosticButton.Content = L("生成诊断 ZIP", "Create Diagnostic ZIP");
        SnapshotTitleTextBlock.Text = L("配置快照", "Configuration Snapshot");
        SnapshotHintTextBlock.Text = L("把主程序配置复制到开发者工具自己的数据目录，不修改主程序。", "Copy main-app configuration into the developer tool data directory without changing the app.");
        CreateSnapshotButton.Content = L("创建配置快照", "Create Configuration Snapshot");
        FoldersTitleTextBlock.Text = L("工作目录", "Working Folders");
        FoldersHintTextBlock.Text = L("快速打开已生成的诊断报告和配置快照。", "Open generated diagnostics and configuration snapshots.");
        OpenDiagnosticsButton.Content = L("诊断报告", "Diagnostics");
        OpenSnapshotsButton.Content = L("配置快照", "Snapshots");
        DiagnosticResultTitleTextBlock.Text = L("操作结果", "Operation Result");

        _applyingSettings = true;
        AutoRefreshToggle.IsOn = _settings.AutoRefresh;
        _applyingSettings = false;
        UpdateTimerState();
        RefreshRepositoryUi();
        RefreshArtifactsList(preserveSelection: true);
    }

    private void ApplyTheme()
    {
        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = _settings.DarkTheme ? ElementTheme.Dark : ElementTheme.Light;
        }
    }

    private async Task RefreshAllAsync(bool showStatus)
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;
        try
        {
            RootPathTextBox.Text = _rootPath;
            _shellConfig = LoadShellConfig();
            RefreshRepositoryCombo();
            RefreshRepositoryUi();
            RefreshArtifactsList(preserveSelection: true);
            await RefreshOverviewAsync();
            if (showStatus)
            {
                SetStatus(L("已刷新独立开发者工具数据。", "Standalone developer-tool data refreshed."), InfoBarSeverity.Success);
            }
        }
        catch (Exception ex)
        {
            SetStatus(L($"刷新失败：{ex.Message}", $"Refresh failed: {ex.Message}"), InfoBarSeverity.Error);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private async Task RefreshOverviewAsync()
    {
        string executablePath = Path.Combine(_rootPath, "ModFolderCopier.WinUI.exe");
        VersionCardValue.Text = File.Exists(executablePath)
            ? FileVersionInfo.GetVersionInfo(executablePath).ProductVersion ?? "-"
            : "-";
        RepositoryCardValue.Text = _shellConfig.Repositories.Count.ToString();

        string[] criticalFiles = ["ModFolderCopier.WinUI.exe", "beta-shell.json", "config.ini"];
        int presentFiles = criticalFiles.Count(name => File.Exists(Path.Combine(_rootPath, name)));
        HealthCardValue.Text = $"{presentFiles}/{criticalFiles.Length}";

        StorageSnapshot storage = await Task.Run(() => ReadStorageSnapshot(_rootPath));
        StorageCardValue.Text = FormatSize(storage.TotalBytes);
        RefreshStorageBreakdown(storage);

        var runtime = new StringBuilder();
        runtime.AppendLine($"{L("开发者工具", "Developer tool")}: v3.9.4");
        runtime.AppendLine($"{L("主程序目录", "Main app root")}: {_rootPath}");
        runtime.AppendLine($"{L("主程序进程", "Main app process")}: {(IsMainAppRunning() ? L("正在运行", "Running") : L("未运行", "Not running"))}");
        runtime.AppendLine($"{L("系统", "OS")}: {RuntimeInformation.OSDescription}");
        runtime.AppendLine($"{L("运行时", "Runtime")}: {RuntimeInformation.FrameworkDescription}");
        runtime.AppendLine($"{L("架构", "Architecture")}: {RuntimeInformation.ProcessArchitecture}");
        runtime.AppendLine($"{L("刷新时间", "Refreshed")}: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        runtime.AppendLine();
        runtime.AppendLine($"beta-shell.json: {FormatFileState(ShellConfigPath)}");
        runtime.AppendLine($"config.ini: {FormatFileState(LegacyConfigPath)}");
        runtime.AppendLine($"app-index.db: {FormatFileState(Path.Combine(_rootPath, "cache", "app-index.db"))}");
        RuntimeTextBox.Text = runtime.ToString();
    }

    private void RefreshStorageBreakdown(StorageSnapshot storage)
    {
        StorageBreakdownPanel.Children.Clear();
        AddStorageRow(L("在线缓存", "Online cache"), storage.CacheBytes);
        AddStorageRow(L("安装备份", "Install backups"), storage.BackupBytes);
        AddStorageRow(L("诊断报告", "Diagnostics"), storage.DiagnosticsBytes);
        AddStorageRow(L("配置文件", "Configuration"), storage.ConfigurationBytes);
    }

    private void AddStorageRow(string label, long bytes)
    {
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = label });
        var value = new TextBlock
        {
            Text = FormatSize(bytes),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        Grid.SetColumn(value, 1);
        grid.Children.Add(value);
        StorageBreakdownPanel.Children.Add(new Border
        {
            Style = (Style)Application.Current.Resources["ToolInsetStyle"],
            Child = grid
        });
    }

    private void RefreshRepositoryCombo()
    {
        string? selectedId = (RepositoryComboBox.SelectedItem as RepositorySnapshot)?.Id
            ?? _shellConfig.SelectedRepositoryId;
        RepositoryComboBox.ItemsSource = null;
        RepositoryComboBox.ItemsSource = _shellConfig.Repositories;
        RepositoryComboBox.SelectedItem = _shellConfig.Repositories.FirstOrDefault(item => item.Id == selectedId)
            ?? _shellConfig.Repositories.FirstOrDefault();
        RepositoryComboBox.PlaceholderText = L("没有可用仓库", "No repositories found");
    }

    private void RefreshRepositoryUi()
    {
        RepositorySnapshot? repository = RepositoryComboBox.SelectedItem as RepositorySnapshot;
        RepositoryPathPanel.Children.Clear();
        if (repository is null)
        {
            RepositoryReportTextBox.Text = L("beta-shell.json 中没有仓库配置。", "No repository configuration was found in beta-shell.json.");
            return;
        }

        AddRepositoryPathRow(L("Mod 仓库", "Mod repository"), repository.SourcePath, Directory.Exists(repository.SourcePath));
        AddRepositoryPathRow(L("目标文件夹", "Target folder"), repository.TargetPath, Directory.Exists(repository.TargetPath));
        AddRepositoryPathRow(L("启动器", "Launcher"), repository.LauncherPath, File.Exists(repository.LauncherPath));
        RepositoryReportTextBox.Text = BuildRepositoryReport(repository);
    }

    private void AddRepositoryPathRow(string label, string path, bool exists)
    {
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { Spacing = 3 };
        text.Children.Add(new TextBlock { Text = label, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        text.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(path) ? L("未配置", "Not configured") : path,
            Style = (Style)Application.Current.Resources["ToolMutedTextStyle"],
            TextWrapping = TextWrapping.Wrap
        });
        grid.Children.Add(text);
        var state = new TextBlock
        {
            Text = exists ? "OK" : "--",
            Foreground = (Brush)Application.Current.Resources[exists ? "ToolSuccessBrush" : "ToolWarningBrush"],
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(state, 1);
        grid.Children.Add(state);
        RepositoryPathPanel.Children.Add(new Border
        {
            Style = (Style)Application.Current.Resources["ToolInsetStyle"],
            Child = grid
        });
    }

    private string BuildRepositoryReport(RepositorySnapshot repository)
    {
        var report = new StringBuilder();
        report.AppendLine($"{L("名称", "Name")}: {repository.Name}");
        report.AppendLine($"ID: {repository.Id}");
        report.AppendLine($"{L("部署方式", "Deployment")}: {(repository.UseDirectoryLinks ? L("目录链接", "Directory links") : L("复制", "Copy"))}");
        report.AppendLine($"GameBanana: {repository.OnlineSourceSite} · {repository.OnlineCategoryId}");
        report.AppendLine($"{L("目标游戏", "Game")}: {repository.OnlineGameName}");
        report.AppendLine($"Wiki: {repository.WikiUrl}");
        report.AppendLine();

        int sourceCount = CountDirectories(repository.SourcePath);
        int targetCount = CountDirectories(repository.TargetPath);
        int linkCount = CountLinkedDirectories(repository.TargetPath);
        report.AppendLine($"{L("仓库顶层目录", "Source top-level folders")}: {sourceCount}");
        report.AppendLine($"{L("目标顶层目录", "Target top-level folders")}: {targetCount}");
        report.AppendLine($"{L("目标目录链接", "Target directory links")}: {linkCount}");
        report.AppendLine($"{L("目录链接模式一致性", "Link-mode consistency")}: {FormatLinkConsistency(repository, targetCount, linkCount)}");
        report.AppendLine();

        int healthy = 0;
        healthy += Directory.Exists(repository.SourcePath) ? 1 : 0;
        healthy += Directory.Exists(repository.TargetPath) ? 1 : 0;
        healthy += File.Exists(repository.LauncherPath) ? 1 : 0;
        report.AppendLine(L($"结论：{healthy}/3 项关键路径可用。", $"Result: {healthy}/3 critical paths are available."));
        return report.ToString();
    }

    private string FormatLinkConsistency(RepositorySnapshot repository, int total, int links)
    {
        if (!repository.UseDirectoryLinks)
        {
            return links == 0 ? L("符合复制模式", "Matches copy mode") : L($"发现 {links} 个目录链接", $"Found {links} directory links");
        }
        if (total == 0)
        {
            return L("目标目录为空", "Target folder is empty");
        }
        return links == total
            ? L("全部目标均为目录链接", "All targets are directory links")
            : L($"{links}/{total} 个目标为目录链接", $"{links}/{total} targets are directory links");
    }

    private void RefreshArtifactsList(bool preserveSelection)
    {
        string? selectedPath = preserveSelection ? (ArtifactListView.SelectedItem as ArtifactItem)?.Path : null;
        _artifacts.Clear();
        AddArtifact("beta-shell.json", L("主界面配置", "Shell configuration"), isJson: true);
        AddArtifact("config.ini", L("兼容配置", "Legacy configuration"));
        AddArtifact("startup.log", L("启动日志", "Startup log"));
        AddArtifact("app-errors.log", L("错误日志", "Error log"));
        AddArtifact("update-agent.log", L("更新代理日志", "Update-agent log"));
        AddArtifact("update.log", L("更新日志", "Update log"));
        AddArtifact(Path.Combine("cache", "app-index.db"), L("本地索引数据库", "Local index database"), isBinary: true);

        ArtifactListView.ItemsSource = null;
        ArtifactListView.ItemsSource = _artifacts;
        ArtifactListView.SelectedItem = _artifacts.FirstOrDefault(item => item.Path == selectedPath)
            ?? _artifacts.FirstOrDefault(item => item.Exists);
    }

    private void AddArtifact(string relativePath, string description, bool isJson = false, bool isBinary = false)
    {
        string path = Path.Combine(_rootPath, relativePath);
        var info = new FileInfo(path);
        _artifacts.Add(new ArtifactItem
        {
            DisplayName = relativePath,
            Description = description,
            Path = path,
            Exists = info.Exists,
            SizeBytes = info.Exists ? info.Length : 0,
            LastWriteTime = info.Exists ? info.LastWriteTime : null,
            IsJson = isJson,
            IsBinary = isBinary
        });
    }

    private void LoadSelectedArtifact()
    {
        if (ArtifactListView.SelectedItem is not ArtifactItem item)
        {
            return;
        }

        FileSearchTextBox.Text = string.Empty;
        if (!item.Exists)
        {
            _activeFileContent = L("文件不存在。", "File does not exist.");
        }
        else if (item.IsBinary)
        {
            _activeFileContent = L(
                $"这是二进制文件，文本查看器不会直接读取内容。\n\n路径：{item.Path}\n大小：{FormatSize(item.SizeBytes)}\n修改时间：{item.LastWriteTime:yyyy-MM-dd HH:mm:ss}",
                $"This is a binary file and is not read directly by the text viewer.\n\nPath: {item.Path}\nSize: {FormatSize(item.SizeBytes)}\nModified: {item.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
        }
        else
        {
            try
            {
                _activeFileContent = File.ReadAllText(item.Path);
            }
            catch (Exception ex)
            {
                _activeFileContent = L($"读取失败：{ex.Message}", $"Read failed: {ex.Message}");
            }
        }

        FileViewerTextBox.Text = _activeFileContent;
        FileViewerStatusTextBlock.Text = $"{item.Description} · {FormatSize(item.SizeBytes)} · {item.Path}";
        ValidateJsonButton.IsEnabled = item.Exists && item.IsJson;
    }

    private ShellConfigSnapshot LoadShellConfig()
    {
        if (!File.Exists(ShellConfigPath))
        {
            return new ShellConfigSnapshot();
        }

        try
        {
            return JsonSerializer.Deserialize<ShellConfigSnapshot>(File.ReadAllText(ShellConfigPath), JsonOptions)
                ?? new ShellConfigSnapshot();
        }
        catch (Exception ex)
        {
            SetStatus(L($"beta-shell.json 解析失败：{ex.Message}", $"Failed to parse beta-shell.json: {ex.Message}"), InfoBarSeverity.Warning);
            return new ShellConfigSnapshot();
        }
    }

    private async void OnBrowseRootClicked(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        StorageFolder? folder = await picker.PickSingleFolderAsync();
        if (folder is null)
        {
            return;
        }

        _rootPath = folder.Path;
        _settings.RootPath = _rootPath;
        SaveSettings();
        await RefreshAllAsync(showStatus: true);
    }

    private async void OnRefreshAllClicked(object sender, RoutedEventArgs e) => await RefreshAllAsync(showStatus: true);

    private void OnRepositorySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_refreshing)
        {
            RefreshRepositoryUi();
        }
    }

    private void OnValidateRepositoryClicked(object sender, RoutedEventArgs e)
    {
        RefreshRepositoryUi();
        SetStatus(L("仓库路径和目录链接状态已重新检查。", "Repository paths and directory-link state checked again."), InfoBarSeverity.Success);
    }

    private void OnOpenRepositoryClicked(object sender, RoutedEventArgs e)
    {
        if (RepositoryComboBox.SelectedItem is RepositorySnapshot repository && Directory.Exists(repository.SourcePath))
        {
            OpenDirectory(repository.SourcePath);
        }
    }

    private void OnArtifactSelectionChanged(object sender, SelectionChangedEventArgs e) => LoadSelectedArtifact();

    private void OnFileSearchChanged(object sender, TextChangedEventArgs e)
    {
        string query = FileSearchTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            FileViewerTextBox.Text = _activeFileContent;
            return;
        }

        string[] matches = _activeFileContent
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .Take(2000)
            .ToArray();
        FileViewerTextBox.Text = matches.Length == 0
            ? L("没有匹配内容。", "No matches.")
            : string.Join(Environment.NewLine, matches);
        FileViewerStatusTextBlock.Text = L($"找到 {matches.Length} 行匹配内容。", $"Found {matches.Length} matching lines.");
    }

    private void OnValidateJsonClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            using JsonDocument _ = JsonDocument.Parse(_activeFileContent);
            SetStatus(L("JSON 结构有效。", "JSON structure is valid."), InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            SetStatus(L($"JSON 无效：{ex.Message}", $"Invalid JSON: {ex.Message}"), InfoBarSeverity.Error);
        }
    }

    private void OnCopyViewerClicked(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(FileViewerTextBox.Text ?? string.Empty);
        Clipboard.SetContent(package);
        Clipboard.Flush();
        SetStatus(L("当前查看内容已复制。", "Current viewer content copied."), InfoBarSeverity.Success);
    }

    private async void OnSaveViewerClicked(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker { SuggestedFileName = "imm-developer-output" };
        picker.FileTypeChoices.Add(L("文本文件", "Text file"), [".txt"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        StorageFile? file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }
        await FileIO.WriteTextAsync(file, FileViewerTextBox.Text ?? string.Empty);
        SetStatus(L("当前内容已保存。", "Current content saved."), InfoBarSeverity.Success);
    }

    private async void OnExportDiagnosticClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(DiagnosticsDirectory);
            string outputPath = Path.Combine(DiagnosticsDirectory, $"IMM-Developer-Diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
            string summary = BuildStandaloneSummary();
            List<ArtifactItem> artifacts = _artifacts
                .Where(item => item.Exists && !item.IsBinary)
                .Select(item => item.Clone())
                .ToList();
            await Task.Run(() => CreateDiagnosticArchive(outputPath, summary, artifacts));
            DiagnosticResultTextBox.Text = L($"已生成脱敏诊断报告：\n{outputPath}", $"Sanitized diagnostic report created:\n{outputPath}");
            SetStatus(L("诊断报告已生成。", "Diagnostic report created."), InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            SetStatus(L($"诊断导出失败：{ex.Message}", $"Diagnostic export failed: {ex.Message}"), InfoBarSeverity.Error);
        }
    }

    private void CreateDiagnosticArchive(string outputPath, string summary, IReadOnlyCollection<ArtifactItem> artifacts)
    {
        using ZipArchive archive = ZipFile.Open(outputPath, ZipArchiveMode.Create);
        WriteArchiveText(archive, "summary.txt", Sanitize(summary));
        foreach (ArtifactItem item in artifacts)
        {
            try
            {
                WriteArchiveText(archive, "files/" + Path.GetFileName(item.Path), Sanitize(File.ReadAllText(item.Path)));
            }
            catch
            {
                // Individual unreadable files do not invalidate the full report.
            }
        }
    }

    private async void OnCreateSnapshotClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            string snapshotPath = Path.Combine(SnapshotsDirectory, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(snapshotPath);
            int count = 0;
            foreach (string path in new[] { ShellConfigPath, LegacyConfigPath })
            {
                if (!File.Exists(path))
                {
                    continue;
                }
                File.Copy(path, Path.Combine(snapshotPath, Path.GetFileName(path)), overwrite: false);
                count++;
            }
            DiagnosticResultTextBox.Text = L($"已创建配置快照，共复制 {count} 个文件：\n{snapshotPath}", $"Configuration snapshot created with {count} files:\n{snapshotPath}");
            SetStatus(L("配置快照已创建，主程序文件未被修改。", "Configuration snapshot created; main-app files were not modified."), InfoBarSeverity.Success);
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            SetStatus(L($"快照创建失败：{ex.Message}", $"Snapshot failed: {ex.Message}"), InfoBarSeverity.Error);
        }
    }

    private void OnOpenDiagnosticsClicked(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(DiagnosticsDirectory);
        OpenDirectory(DiagnosticsDirectory);
    }

    private void OnOpenSnapshotsClicked(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(SnapshotsDirectory);
        OpenDirectory(SnapshotsDirectory);
    }

    private void OnThemeClicked(object sender, RoutedEventArgs e)
    {
        _settings.DarkTheme = !_settings.DarkTheme;
        ApplyTheme();
        ApplyLanguage();
        SaveSettings();
    }

    private void OnLanguageClicked(object sender, RoutedEventArgs e)
    {
        _settings.English = !_settings.English;
        ApplyLanguage();
        SaveSettings();
        _ = RefreshAllAsync(showStatus: false);
    }

    private void OnAutoRefreshToggled(object sender, RoutedEventArgs e)
    {
        if (_applyingSettings)
        {
            return;
        }
        _settings.AutoRefresh = AutoRefreshToggle.IsOn;
        UpdateTimerState();
        SaveSettings();
    }

    private void UpdateTimerState()
    {
        if (_settings.AutoRefresh)
        {
            _refreshTimer.Start();
        }
        else
        {
            _refreshTimer.Stop();
        }
    }

    private string BuildStandaloneSummary()
    {
        var text = new StringBuilder();
        text.AppendLine("Integrated Mod Manager Developer Tools");
        text.AppendLine($"Generated: {DateTimeOffset.Now:O}");
        text.AppendLine($"Root: {_rootPath}");
        text.AppendLine($"OS: {RuntimeInformation.OSDescription}");
        text.AppendLine($"Runtime: {RuntimeInformation.FrameworkDescription}");
        text.AppendLine($"Repositories: {_shellConfig.Repositories.Count}");
        foreach (RepositorySnapshot repository in _shellConfig.Repositories)
        {
            text.AppendLine();
            text.AppendLine(BuildRepositoryReport(repository));
        }
        return text.ToString();
    }

    private string Sanitize(string value)
    {
        string sanitized = value;
        if (!string.IsNullOrWhiteSpace(_rootPath))
        {
            sanitized = sanitized.Replace(_rootPath, "<MAIN_APP_ROOT>", StringComparison.OrdinalIgnoreCase);
        }
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            sanitized = sanitized.Replace(userProfile, "<USER_PROFILE>", StringComparison.OrdinalIgnoreCase);
        }
        return sanitized.Replace(Environment.UserName, "<USER>", StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteArchiveText(ZipArchive archive, string name, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using StreamWriter writer = new(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private void OpenDirectory(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetStatus(L($"无法打开目录：{ex.Message}", $"Could not open folder: {ex.Message}"), InfoBarSeverity.Error);
        }
    }

    private void SetStatus(string message, InfoBarSeverity severity)
    {
        StatusInfoBar.Message = message;
        StatusInfoBar.Severity = severity;
        StatusInfoBar.IsOpen = true;
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                _settings = JsonSerializer.Deserialize<DeveloperToolSettings>(File.ReadAllText(SettingsPath), JsonOptions)
                    ?? new DeveloperToolSettings();
            }
        }
        catch
        {
            _settings = new DeveloperToolSettings();
        }
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(_settingsDirectory);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(_settings, JsonOptions));
        }
        catch
        {
            // Settings persistence must not block diagnostics.
        }
    }

    private string ResolveInitialRoot(string configuredRoot)
    {
        if (Directory.Exists(configuredRoot))
        {
            return Path.GetFullPath(configuredRoot);
        }

        string baseDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        if (File.Exists(Path.Combine(baseDirectory, "ModFolderCopier.WinUI.exe")))
        {
            return baseDirectory;
        }

        string current = baseDirectory;
        for (int depth = 0; depth < 9; depth++)
        {
            string releaseRoot = Path.Combine(current, "WinUI3", "bin", "x64", "Release", "net8.0-windows10.0.19041.0", "win-x64");
            if (File.Exists(Path.Combine(releaseRoot, "ModFolderCopier.WinUI.exe")))
            {
                return releaseRoot;
            }
            DirectoryInfo? parent = Directory.GetParent(current);
            if (parent is null)
            {
                break;
            }
            current = parent.FullName;
        }
        return baseDirectory;
    }

    private static StorageSnapshot ReadStorageSnapshot(string root)
    {
        long cache = GetDirectorySize(Path.Combine(root, "cache"));
        long backups = GetDirectorySize(Path.Combine(root, "backups"));
        long diagnostics = GetDirectorySize(Path.Combine(root, "diagnostics"));
        long configuration = GetFileSize(Path.Combine(root, "beta-shell.json")) + GetFileSize(Path.Combine(root, "config.ini"));
        return new StorageSnapshot(cache, backups, diagnostics, configuration);
    }

    private static long GetDirectorySize(string path)
    {
        if (!Directory.Exists(path))
        {
            return 0;
        }
        try
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Sum(file =>
                {
                    try { return new FileInfo(file).Length; }
                    catch { return 0L; }
                });
        }
        catch
        {
            return 0;
        }
    }

    private static long GetFileSize(string path)
    {
        try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
        catch { return 0; }
    }

    private static int CountDirectories(string path)
    {
        try { return Directory.Exists(path) ? Directory.EnumerateDirectories(path).Count() : 0; }
        catch { return 0; }
    }

    private static int CountLinkedDirectories(string path)
    {
        try { return Directory.Exists(path) ? Directory.EnumerateDirectories(path).Count(DirectoryLinkDeployment.IsDirectoryLink) : 0; }
        catch { return 0; }
    }

    private static bool IsMainAppRunning()
    {
        try { return Process.GetProcessesByName("ModFolderCopier.WinUI").Length > 0; }
        catch { return false; }
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }

    private static string FormatFileState(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? $"{FormatSize(info.Length)} · {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}" : "missing";
        }
        catch
        {
            return "unavailable";
        }
    }
}

internal sealed class DeveloperToolSettings
{
    public string RootPath { get; set; } = string.Empty;
    public bool English { get; set; }
    public bool DarkTheme { get; set; } = true;
    public bool AutoRefresh { get; set; }
}

internal sealed class ShellConfigSnapshot
{
    public string? SelectedRepositoryId { get; set; }
    public List<RepositorySnapshot> Repositories { get; set; } = [];
}

internal sealed class RepositorySnapshot
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public string LauncherPath { get; set; } = string.Empty;
    public bool UseDirectoryLinks { get; set; }
    public string OnlineSourceSite { get; set; } = string.Empty;
    public string OnlineGameName { get; set; } = string.Empty;
    public string OnlineCategoryId { get; set; } = string.Empty;
    public string WikiUrl { get; set; } = string.Empty;
    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? Id : Name;
}

internal sealed class ArtifactItem
{
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool Exists { get; set; }
    public long SizeBytes { get; set; }
    public DateTime? LastWriteTime { get; set; }
    public bool IsJson { get; set; }
    public bool IsBinary { get; set; }
    public ArtifactItem Clone() => (ArtifactItem)MemberwiseClone();
    public override string ToString() => Exists ? $"{DisplayName}  ·  {MainWindowFormat.FormatSize(SizeBytes)}" : $"{DisplayName}  ·  --";
}

internal readonly record struct StorageSnapshot(long CacheBytes, long BackupBytes, long DiagnosticsBytes, long ConfigurationBytes)
{
    public long TotalBytes => CacheBytes + BackupBytes + DiagnosticsBytes + ConfigurationBytes;
}

internal static class MainWindowFormat
{
    public static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }
}
