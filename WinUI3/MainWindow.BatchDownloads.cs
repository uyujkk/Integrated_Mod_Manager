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
        { await ShowMessageAsync(T("BatchDownloads.TheDownloadRecordIsUnreadableAnd"), T("BatchDownloads.CannotDownloadBatch")); return; }
        var context = CaptureOnlineDownloadContext(repository);
        string contextKey = GetOnlineDetailContextKey();
        bool endfield = IsEndfieldRepository(repository);
        if (!Directory.Exists(context.RepositoryPath))
        { await ShowMessageAsync(T("BatchDownloads.TheRepositoryFolderIsUnavailableCorrect"), T("BatchDownloads.CannotDownloadBatch")); return; }
        using var cancellation = new CancellationTokenSource();
        var progress = new TextBlock { Text = T("BatchDownloads.ReadingFileLists"), TextWrapping = TextWrapping.Wrap };
        var progressDialog = new ContentDialog { Title = T("BatchDownloads.PrepareBatchDownloads"), Content = progress, CloseButtonText = T("Common.Cancel"),
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
                progress.Text = T("BatchDownloads.Reading", rows.Count + 1, saved.Length, item.Mod.Title);
                try
                {
                    if (item.RepositoryId != context.RepositoryId || !string.Equals(item.RepositoryPath.TrimEnd('\\', '/'), context.RepositoryPath.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException(T("BatchDownloads.TheSavedRepositoryPathHasChanged"));
                    if (item.Mod.ItemId <= 0)
                        throw new InvalidDataException(T("BatchDownloads.TheSiteEntryIDIsMissing"));
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
        body.Children.Add(new TextBlock { Text = T("BatchDownloads.RepositoryReviewFilesAndDestinationsBefore", context.RepositoryName, context.RepositoryPath), TextWrapping = TextWrapping.Wrap });
        var allowDuplicates = new CheckBox { Content = T("BatchDownloads.AllowSeparateCopiesOfInstalledEntries") };
        body.Children.Add(allowDuplicates);
        var summary = new TextBlock { TextWrapping = TextWrapping.Wrap }; body.Children.Add(summary);
        var plans = new List<(PreparedSavedDownload Data, CheckBox Include, ComboBox File, ComboBox Folder, TextBlock Status)>();
        var dialog = new ContentDialog { Title = T("BatchDownloads.ReviewBatchDownloads"), PrimaryButtonText = T("BatchDownloads.AddToDownloadQueue"), CloseButtonText = T("Common.Cancel"),
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
                plan.Status.Text = plan.Data.Error ?? (file is null ? T("BatchDownloads.NoSupportedArchiveSkipped") : kind switch
                {
                    OnlineDuplicateKind.SameFile => T("BatchDownloads.SameFileInstalledASeparateCopy"),
                    OnlineDuplicateKind.OtherFile => T("BatchDownloads.AnotherFileOrVariantIsInstalled"),
                    OnlineDuplicateKind.UnknownFile => T("BatchDownloads.EntryInstalledTheOlderRecordCannot"),
                    _ => T("BatchDownloads.Ready")
                });
                if (plan.Include.IsChecked != true || !plan.Include.IsEnabled) continue;
                count++;
                if (kind != OnlineDuplicateKind.None) duplicateCount++;
            }
            summary.Text = T("BatchDownloads.SelectedHaveExistingInstallations", count, duplicateCount);
            dialog.IsPrimaryButtonEnabled = count > 0 && (duplicateCount == 0 || allowDuplicates.IsChecked == true);
        }
        foreach (var data in rows)
        {
            var panel = new StackPanel { Spacing = 6 };
            var file = new ComboBox { Header = T("BatchDownloads.DownloadFile"), HorizontalAlignment = HorizontalAlignment.Stretch };
            var candidates = OnlineDownloadSelectionPolicy.OrderForManualSelection(data.Mod.DownloadFiles).Where(candidate => candidate.IsSupportedArchive).ToArray();
            if (candidates.Length == 0 && data.Mod.DownloadFiles.Count == 0 && IsSupportedArchiveFile(data.Mod.DownloadFileName) && !string.IsNullOrWhiteSpace(data.Mod.DownloadUrl))
                candidates = [new OnlineDownloadCandidate { FileId = data.Mod.DownloadFileId, FileName = data.Mod.DownloadFileName, DownloadUrl = data.Mod.DownloadUrl, IsSupportedArchive = true }];
            foreach (var candidate in candidates) file.Items.Add(new ComboBoxItem { Content = candidate.FileName + (candidate.IsArchived ? T("BatchDownloads.Archived") : ""), Tag = candidate });
            if (file.Items.Count > 0) file.SelectedIndex = 0;
            var include = new CheckBox { Content = data.Mod.Title, IsEnabled = data.Error is null && candidates.Length > 0,
                IsChecked = data.Error is null && candidates.Length > 0 && !_onlineDownloadActions.IsActive(context, GetOnlineDownloadIdentity(data.Mod)) };
            if (_onlineDownloadActions.IsActive(context, GetOnlineDownloadIdentity(data.Mod))) { include.IsEnabled = false; include.Content += T("BatchDownloads.TaskActive"); }
            panel.Children.Add(include); panel.Children.Add(file);
            var folder = new ComboBox { Header = T("BatchDownloads.DownloadAndExtractInto"), HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (string path in folders) folder.Items.Add(new ComboBoxItem { Content = path == context.RepositoryPath ? T("BatchDownloads.RepositoryRoot") : Path.GetFileName(path), Tag = path });
            // Issue #6: reuse a matching character folder, or offer to create one; nothing is created until approval.
            CharacterFolderPlan? folderPlan = data.Error is null ? PlanCharacterFolder(data.Mod, context.RepositoryPath, data.Aliases) : null;
            string? preferred = folderPlan?.Action == CharacterFolderAction.UseExisting ? folderPlan.Path : null;
            ComboBoxItem? createItem = null;
            if (folderPlan?.Action == CharacterFolderAction.Create)
            {
                createItem = new ComboBoxItem { Content = T("BatchDownloads.New", Path.GetFileName(folderPlan.Path)), Tag = new NewCharacterFolderChoice(folderPlan.Path!) };
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
                        ? T("BatchDownloads.CreatedAfterApproval", folderPlan.Path)
                        : CharacterFolderReasonText(folderPlan, character),
                    TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = .75
                });
            }
            var status = new TextBlock { TextWrapping = TextWrapping.Wrap }; panel.Children.Add(status);
            AutomationProperties.SetName(file, T("BatchDownloads.DownloadFile2") + data.Mod.Title);
            AutomationProperties.SetName(folder, T("BatchDownloads.DownloadDestination") + data.Mod.Title);
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
        { await ShowMessageAsync(T("BatchDownloads.ADestinationIsNoLongerAvailable"), T("BatchDownloads.DestinationChanged")); return; }
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
            await ShowMessageAsync(T("BatchDownloads.CouldNotCreateACharacterFolder") + ex.Message, T("BatchDownloads.DestinationChanged"));
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
