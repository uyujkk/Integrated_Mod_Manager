using IntegratedModManager.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private readonly OnlineDownloadScheduler _onlineDownloadScheduler = new(2);

    private async void OnOnlineDownloadOptionsClicked(object sender, RoutedEventArgs e)
    {
        var concurrency = new ComboBox { Header = L("同时执行的下载任务", "Concurrent download tasks"), HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (int value in Enumerable.Range(1, 4)) concurrency.Items.Add(value);
        concurrency.SelectedItem = _onlinePreferences.ConcurrentDownloads;
        var remember = new CheckBox { Content = L("按仓库和角色记住确认过的下载目录", "Remember confirmed download folders per repository and character"), IsChecked = _onlinePreferences.RememberDownloadFolders };
        var body = new StackPanel { Spacing = 12, MinWidth = 300 };
        body.Children.Add(concurrency); body.Children.Add(remember);
        body.Children.Add(new TextBlock { Text = L("记住的目录必须仍位于原仓库内；目录失效时会重新询问。关闭此选项可恢复每次确认。", "Remembered folders must still be inside the original repository. Invalid folders prompt again. Turn this off to confirm every time."), TextWrapping = TextWrapping.Wrap, MaxWidth = 440 });
        var dialog = new ContentDialog { Title = L("下载选项", "Download options"), Content = body, PrimaryButtonText = L("保存", "Save"), CloseButtonText = L("取消", "Cancel"), XamlRoot = RootGrid.XamlRoot, RequestedTheme = RootGrid.ActualTheme };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        _onlinePreferences.ConcurrentDownloads = (int)concurrency.SelectedItem;
        _onlinePreferences.RememberDownloadFolders = remember.IsChecked == true;
        _onlineDownloadScheduler.Limit = _onlinePreferences.ConcurrentDownloads;
        SaveShellConfig();
    }

    private static string DownloadFolderKey(OnlineDownloadContext context, OnlineModCard mod)
        => System.Text.Json.JsonSerializer.Serialize(new[] { context.RepositoryId, context.RepositoryPath.TrimEnd('\\', '/'), mod.CharacterName });

    private bool IsUsableRememberedFolder(string? path, OnlineDownloadContext context)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(context.RepositoryPath)
                && Directory.Exists(path) && (string.Equals(Path.GetFullPath(path).TrimEnd('\\'), Path.GetFullPath(context.RepositoryPath).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                    || IsPathInsideDirectory(path, context.RepositoryPath));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException) { return false; }
    }

    private async Task<string?> ResolveOnlineDownloadFolderAsync(OnlineModCard mod, OnlineDownloadContext context,
        IReadOnlyCollection<string> aliases, string? confirmedDestination)
    {
        // Retry is explicitly routed to its original confirmed destination, never the current picker.
        if (confirmedDestination is not null)
        {
            if (!Directory.Exists(confirmedDestination))
                throw new DirectoryNotFoundException(L("原下载目录已不存在，请从 Mod 卡片重新选择位置。", "The original destination no longer exists. Start from the mod card to choose a new folder."));
            return confirmedDestination;
        }
        string key = DownloadFolderKey(context, mod);
        if (_onlinePreferences.RememberDownloadFolders && _onlinePreferences.DownloadFolders.TryGetValue(key, out string? remembered)
            && IsUsableRememberedFolder(remembered, context)) return remembered;
        string? selected = await PickDownloadFolderAsync(mod, context, aliases);
        if (_onlinePreferences.RememberDownloadFolders && IsUsableRememberedFolder(selected, context))
        {
            _onlinePreferences.DownloadFolders[key] = selected!;
            SaveShellConfig();
        }
        return selected;
    }

    private async Task<bool> ConfirmOnlineDuplicateAsync(OnlineModCard mod, OnlineDownloadCandidate? file, OnlineDownloadContext context)
    {
        TrackedModOrigin[] installed = _trackedModOrigins.Values.Where(origin =>
            OnlineInstallationDetectionPolicy.Matches(mod.ItemId, mod.ProfileUrl, origin.ItemId, origin.ProfileUrl)
            && Directory.Exists(origin.Path) && !string.IsNullOrWhiteSpace(context.RepositoryPath)
            && IsPathInsideDirectory(origin.Path, context.RepositoryPath)).ToArray();
        OnlineDuplicateKind kind = OnlineDuplicatePolicy.Classify(file?.FileId, installed.Select(item => item.DownloadFileId));
        if (kind == OnlineDuplicateKind.None) return true;
        string summary = kind switch
        {
            OnlineDuplicateKind.SameFile => L("当前仓库已安装相同下载文件。", "This repository already contains this download file."),
            OnlineDuplicateKind.OtherFile => L("当前仓库已安装该条目的其他文件，可能是不同版本或变体。", "This repository contains another file from this entry, possibly a different version or variant."),
            _ => L("当前仓库已安装该条目，但旧记录不足以确认是否为同一文件。", "This repository contains this entry, but its older record cannot identify the exact file.")
        };
        var body = new TextBlock { Text = summary + "\n\n" + string.Join("\n", installed.Take(5).Select(item => item.Path))
            + L("\n\n继续会创建独立副本。", "\n\nContinuing creates a separate copy."), TextWrapping = TextWrapping.Wrap, MaxWidth = 560 };
        var dialog = new ContentDialog { Title = L("发现已有安装", "Existing installation found"), Content = body,
            PrimaryButtonText = L("仍然下载独立副本", "Download a separate copy"), CloseButtonText = L("取消", "Cancel"),
            DefaultButton = ContentDialogButton.Close, XamlRoot = RootGrid.XamlRoot, RequestedTheme = RootGrid.ActualTheme };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task RetryOnlineDownloadAsync(DownloadTaskItem task)
    {
        if (task.RetryRequest is not { } request || task.RetryInProgress || _onlineDownloadActions.IsPreparing) return;
        task.RetryInProgress = true;
        RefreshDownloadTaskCard(task);
        try
        {
            if (!await ConfirmRestoredDownloadAsync(task, request)) return;
            await DownloadAndExtractOnlineModAsync(request.Mod, request.File, refreshDownloadInfo: true,
                capturedContext: request.Context, capturedEndfieldMetadata: request.EndfieldMetadata,
                capturedCharacterAliases: request.CharacterAliases, confirmedDestination: request.Destination);
        }
        catch (Exception ex) { await ReportOnlineActionErrorAsync(ex.Message, L("无法恢复下载", "Cannot restart download")); }
        finally { task.RetryInProgress = false; if (!_onlineWindowClosed) RefreshDownloadTaskCard(task); }
    }
}

public sealed record OnlineDownloadRetryRequest(OnlineModCard Mod, OnlineDownloadCandidate? File,
    OnlineDownloadContext Context, bool EndfieldMetadata, IReadOnlyCollection<string> CharacterAliases, string Destination);
