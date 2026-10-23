using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Online;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private sealed record PreparedSavedDownload(OnlineModCard Mod, IReadOnlyCollection<string> Aliases, string? Error);

    private OnlineDuplicateKind GetOnlineDuplicateKind(OnlineModCard mod, OnlineDownloadCandidate? file, OnlineDownloadContext context)
        => OnlineDuplicatePolicy.Classify(file?.FileId, _trackedModOrigins.Values.Where(origin =>
            OnlineInstallationDetectionPolicy.Matches(mod.ItemId, mod.ProfileUrl, origin.ItemId, origin.ProfileUrl)
            && Directory.Exists(origin.Path) && IsPathInsideDirectory(origin.Path, context.RepositoryPath)).Select(origin => origin.DownloadFileId));

    private async Task PrepareSavedBatchAsync(OnlineSavedMod[] saved, WorkspaceRepository repository)
    {
        if (_onlineDownloadActions.IsPreparing || saved.Length == 0) return;
        if (!_downloadJournalAvailable)
        { await ShowMessageAsync(L("下载记录无法读取，已保留原文件。请先检查诊断报告。", "The download record is unreadable and has been preserved. Review diagnostics first."), L("无法批量下载", "Cannot download batch")); return; }
        var context = CaptureOnlineDownloadContext(repository);
        string contextKey = GetOnlineDetailContextKey();
        bool endfield = IsEndfieldRepository(repository);
        if (!Directory.Exists(context.RepositoryPath))
        { await ShowMessageAsync(L("仓库目录不存在，请先修正路径。", "The repository folder is unavailable. Correct its path first."), L("无法批量下载", "Cannot download batch")); return; }
        using var cancellation = new CancellationTokenSource();
        var progress = new TextBlock { Text = L("正在读取文件列表…", "Reading file lists…"), TextWrapping = TextWrapping.Wrap };
        var progressDialog = new ContentDialog { Title = L("准备批量下载", "Prepare batch downloads"), Content = progress, CloseButtonText = L("取消", "Cancel"),
            XamlRoot = RootGrid.XamlRoot, RequestedTheme = RootGrid.ActualTheme };
        bool prepared = false;
        progressDialog.Closing += (_, _) => { if (!prepared) cancellation.Cancel(); };
        var showing = progressDialog.ShowAsync();
        var rows = new List<PreparedSavedDownload>();
        try
        {
            foreach (var item in saved.DistinctBy(item => GetOnlineDownloadIdentity(item.Mod)))
            {
                cancellation.Token.ThrowIfCancellationRequested();
                progress.Text = L($"正在读取 {rows.Count + 1}/{saved.Length}：{item.Mod.Title}", $"Reading {rows.Count + 1}/{saved.Length}: {item.Mod.Title}");
                try
                {
                    if (item.RepositoryId != context.RepositoryId || !string.Equals(item.RepositoryPath.TrimEnd('\\', '/'), context.RepositoryPath.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException(L("收藏时的仓库路径已改变，请重新收藏。", "The saved repository path has changed. Save this Mod again."));
                    if (item.Mod.ItemId <= 0)
                        throw new InvalidDataException(L("站点条目编号缺失，无法读取安装详情；请从有效 Mod 页面重新收藏。", "The site entry ID is missing, so installation details cannot be loaded. Save this Mod again from a valid page."));
                    var mod = await RefreshOnlineModDownloadInfoAsync(item.Mod, endfield, cancellation.Token, requireDownloadInfo: true);
                    rows.Add(new(mod, GetCharacterFolderAliases(mod).ToArray(), null));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { rows.Add(new(item.Mod, [], ex.Message)); }
            }
            prepared = true;
        }
        catch (OperationCanceledException) { }
        finally { progressDialog.Hide(); await showing; }
        if (!prepared || cancellation.IsCancellationRequested || _onlineWindowClosed || contextKey != GetOnlineDetailContextKey()) return;

        var body = new StackPanel { Spacing = 14, MinWidth = 320, MaxWidth = 620 };
        body.Children.Add(new TextBlock { Text = L($"仓库：{context.RepositoryName}\n{context.RepositoryPath}\n逐项确认文件和目录，确认后才开始下载。失败项可在任务中心单独重试。", $"Repository: {context.RepositoryName}\n{context.RepositoryPath}\nReview files and destinations before downloading. Failed tasks can be retried individually."), TextWrapping = TextWrapping.Wrap });
        var allowDuplicates = new CheckBox { Content = L("允许为已有条目下载独立副本（包含不同变体与旧记录）", "Allow separate copies of installed entries (including variants and older records)") };
        body.Children.Add(allowDuplicates);
        var summary = new TextBlock { TextWrapping = TextWrapping.Wrap }; body.Children.Add(summary);
        var plans = new List<(PreparedSavedDownload Data, CheckBox Include, ComboBox File, ComboBox Folder, TextBlock Status)>();
        var dialog = new ContentDialog { Title = L("确认批量下载清单", "Review batch downloads"), PrimaryButtonText = L("加入下载队列", "Add to download queue"), CloseButtonText = L("取消", "Cancel"),
            Content = new ScrollViewer { Content = body, MaxHeight = Math.Max(180, RootGrid.ActualHeight - 280) }, XamlRoot = RootGrid.XamlRoot, RequestedTheme = RootGrid.ActualTheme };
        dialog.Resources["ContentDialogMaxWidth"] = Math.Min(760, Math.Max(400, RootGrid.ActualWidth - 80));
        string[] folders = Directory.GetDirectories(context.RepositoryPath).Order(StringComparer.CurrentCultureIgnoreCase).Prepend(context.RepositoryPath).ToArray();
        void Validate()
        {
            int count = 0, duplicateCount = 0;
            foreach (var plan in plans)
            {
                var file = (plan.File.SelectedItem as ComboBoxItem)?.Tag as OnlineDownloadCandidate;
                var kind = GetOnlineDuplicateKind(plan.Data.Mod, file, context);
                plan.Status.Text = plan.Data.Error ?? (file is null ? L("没有可自动解压的文件，将跳过。", "No supported archive; skipped.") : kind switch
                {
                    OnlineDuplicateKind.SameFile => L("已安装相同文件：会创建独立副本。", "Same file installed: a separate copy will be created."),
                    OnlineDuplicateKind.OtherFile => L("已安装其他文件或变体：会创建独立副本。", "Another file or variant is installed: a separate copy will be created."),
                    OnlineDuplicateKind.UnknownFile => L("已安装此条目，旧记录无法确认具体文件。", "Entry installed; the older record cannot identify its file."),
                    _ => L("准备就绪", "Ready")
                });
                if (plan.Include.IsChecked != true || !plan.Include.IsEnabled) continue;
                count++;
                if (kind != OnlineDuplicateKind.None) duplicateCount++;
            }
            summary.Text = L($"已选择 {count} 项；其中 {duplicateCount} 项已有安装。", $"Selected {count}; {duplicateCount} have existing installations.");
            dialog.IsPrimaryButtonEnabled = count > 0 && (duplicateCount == 0 || allowDuplicates.IsChecked == true);
        }
        foreach (var data in rows)
        {
            var panel = new StackPanel { Spacing = 6 };
            var file = new ComboBox { Header = L("下载文件", "Download file"), HorizontalAlignment = HorizontalAlignment.Stretch };
            var candidates = OnlineDownloadSelectionPolicy.OrderForManualSelection(data.Mod.DownloadFiles).Where(candidate => candidate.IsSupportedArchive).ToArray();
            if (candidates.Length == 0 && data.Mod.DownloadFiles.Count == 0 && IsSupportedArchiveFile(data.Mod.DownloadFileName) && !string.IsNullOrWhiteSpace(data.Mod.DownloadUrl))
                candidates = [new OnlineDownloadCandidate { FileId = data.Mod.DownloadFileId, FileName = data.Mod.DownloadFileName, DownloadUrl = data.Mod.DownloadUrl, IsSupportedArchive = true }];
            foreach (var candidate in candidates) file.Items.Add(new ComboBoxItem { Content = candidate.FileName + (candidate.IsArchived ? L("（历史文件）", " (archived)") : ""), Tag = candidate });
            if (file.Items.Count > 0) file.SelectedIndex = 0;
            var include = new CheckBox { Content = data.Mod.Title, IsEnabled = data.Error is null && candidates.Length > 0,
                IsChecked = data.Error is null && candidates.Length > 0 && !_onlineDownloadActions.IsActive(context, GetOnlineDownloadIdentity(data.Mod)) };
            if (_onlineDownloadActions.IsActive(context, GetOnlineDownloadIdentity(data.Mod))) { include.IsEnabled = false; include.Content += L("（已有活动任务）", " (task active)"); }
            panel.Children.Add(include); panel.Children.Add(file);
            var folder = new ComboBox { Header = L("下载并解压到", "Download and extract into"), HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (string path in folders) folder.Items.Add(new ComboBoxItem { Content = path == context.RepositoryPath ? L("仓库根目录", "Repository root") : Path.GetFileName(path), Tag = path });
            // Issue #6: reuse a matching character folder, or offer to create one; nothing is created until approval.
            CharacterFolderPlan? folderPlan = data.Error is null ? PlanCharacterFolder(data.Mod, context.RepositoryPath, data.Aliases) : null;
            string? preferred = folderPlan?.Action == CharacterFolderAction.UseExisting ? folderPlan.Path : null;
            ComboBoxItem? createItem = null;
            if (folderPlan?.Action == CharacterFolderAction.Create)
            {
                createItem = new ComboBoxItem { Content = L($"新建：{Path.GetFileName(folderPlan.Path)}", $"New: {Path.GetFileName(folderPlan.Path)}"), Tag = new NewCharacterFolderChoice(folderPlan.Path!) };
                folder.Items.Insert(1, createItem);
            }
            string folderKey = DownloadFolderKey(context, data.Mod);
            bool usesRemembered = false;
            if (_onlinePreferences.RememberDownloadFolders && _onlinePreferences.DownloadFolders.TryGetValue(folderKey, out var remembered) && IsUsableRememberedFolder(remembered, context))
            {
                preferred = remembered;
                usesRemembered = true;
                if (!folders.Contains(remembered, StringComparer.OrdinalIgnoreCase)) folder.Items.Add(new ComboBoxItem { Content = remembered, Tag = remembered });
            }
            folder.SelectedItem = folder.Items.OfType<ComboBoxItem>().FirstOrDefault(item => string.Equals(item.Tag as string, preferred, StringComparison.OrdinalIgnoreCase))
                ?? (usesRemembered ? null : createItem) ?? folder.Items[0];
            panel.Children.Add(folder);
            if (folderPlan is { Action: not CharacterFolderAction.UseExisting })
            {
                string character = GetOnlineCharacterDisplayName(data.Mod.CharacterName);
                panel.Children.Add(new TextBlock
                {
                    Text = folderPlan.Action == CharacterFolderAction.Create
                        ? L($"确认后新建：{folderPlan.Path}", $"Created after approval: {folderPlan.Path}")
                        : CharacterFolderReasonText(folderPlan, character),
                    TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = .75
                });
            }
            var status = new TextBlock { TextWrapping = TextWrapping.Wrap }; panel.Children.Add(status);
            AutomationProperties.SetName(file, L("下载文件：", "Download file: ") + data.Mod.Title);
            AutomationProperties.SetName(folder, L("下载目录：", "Download destination: ") + data.Mod.Title);
            plans.Add((data, include, file, folder, status));
            include.Click += (_, _) => Validate(); file.SelectionChanged += (_, _) => Validate();
            body.Children.Add(new Border { Child = panel, Padding = new Thickness(12), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), BorderBrush = GetAppThemeBrush("AppNavSelectedBorderBrush") });
        }
        allowDuplicates.Click += (_, _) => Validate();
        Validate();
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || _onlineWindowClosed || contextKey != GetOnlineDetailContextKey()) return;
        var chosen = plans.Where(plan => plan.Include.IsChecked == true && plan.Include.IsEnabled).Select(plan => (
            plan.Data, File: (OnlineDownloadCandidate)((ComboBoxItem)plan.File.SelectedItem).Tag,
            Choice: ((ComboBoxItem)plan.Folder.SelectedItem).Tag)).ToArray();
        if (chosen.Any(plan => plan.Choice is string folder && !IsUsableRememberedFolder(folder, context)))
        { await ShowMessageAsync(L("清单中有目录已失效，请重新准备批量下载。", "A destination is no longer available. Prepare the batch again."), L("目录已改变", "Destination changed")); return; }
        if (allowDuplicates.IsChecked != true && chosen.Any(plan => GetOnlineDuplicateKind(plan.Data.Mod, plan.File, context) != OnlineDuplicateKind.None)) return;
        // Only approved rows create folders; Mods of the same character share one creation.
        var created = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var choice in chosen.Select(plan => plan.Choice).OfType<NewCharacterFolderChoice>().DistinctBy(choice => choice.Path, StringComparer.OrdinalIgnoreCase))
                created[choice.Path] = CharacterFolderPlanner.CreatePlannedFolder(context.RepositoryPath, choice.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowMessageAsync(L("无法新建角色文件夹，未加入任何下载：", "Could not create a character folder; nothing was queued: ") + ex.Message, L("目录已改变", "Destination changed"));
            return;
        }
        var approved = chosen.Select(plan => (plan.Data, plan.File,
            Folder: plan.Choice is NewCharacterFolderChoice choice ? created[choice.Path] : (string)plan.Choice)).ToArray();
        UiCrashDiagnostics.RecordAction("Batch download confirmed");
        var tasks = new List<Task>();
        foreach (var plan in approved)
        {
            if (_onlineWindowClosed || contextKey != GetOnlineDetailContextKey()) break;
            tasks.Add(DownloadAndExtractOnlineModAsync(plan.Data.Mod, plan.File, refreshDownloadInfo: false,
                capturedContext: context, capturedEndfieldMetadata: endfield, capturedCharacterAliases: plan.Data.Aliases,
                confirmedDestination: plan.Folder, batchConfirmed: true));
        }
        await Task.WhenAll(tasks);
    }
}
