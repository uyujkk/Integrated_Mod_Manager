using IntegratedModManager.Core.Configuration;
using IntegratedModManager.Core.Online;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private readonly OnlineDownloadScheduler _onlineDownloadScheduler = new(2);

    private async void OnOnlineDownloadOptionsClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var concurrency = new ComboBox { Header = T("OnlineDownloads.ConcurrentDownloadTasks"), HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (int value in Enumerable.Range(1, 4)) concurrency.Items.Add(value);
            concurrency.SelectedItem = _onlinePreferences.ConcurrentDownloads;
            var remember = new CheckBox { Content = T("OnlineDownloads.RememberConfirmedDownloadFoldersPerRepository"), IsChecked = _onlinePreferences.RememberDownloadFolders };
            var body = new StackPanel { Spacing = 12, MinWidth = 300 };
            body.Children.Add(concurrency); body.Children.Add(remember);
            body.Children.Add(new TextBlock { Text = T("OnlineDownloads.RememberedFoldersMustStillBeInside"), TextWrapping = TextWrapping.Wrap, MaxWidth = 440 });
            var dialog = new ContentDialog { Title = T("Common.DownloadOptions"), Content = body, PrimaryButtonText = T("Common.Save"), CloseButtonText = T("Common.Cancel"), XamlRoot = RootGrid.XamlRoot, RequestedTheme = RootGrid.ActualTheme };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            _onlinePreferences.ConcurrentDownloads = (int)concurrency.SelectedItem;
            _onlinePreferences.RememberDownloadFolders = remember.IsChecked == true;
            _onlineDownloadScheduler.Limit = _onlinePreferences.ConcurrentDownloads;
            SaveShellConfig();
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnOnlineDownloadOptionsClicked", ex);
        }
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
                throw new DirectoryNotFoundException(T("OnlineDownloads.TheOriginalDestinationNoLongerExists"));
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
            OnlineDuplicateKind.SameFile => T("OnlineDownloads.ThisRepositoryAlreadyContainsThisDownload"),
            OnlineDuplicateKind.OtherFile => T("OnlineDownloads.ThisRepositoryContainsAnotherFileFrom"),
            _ => T("OnlineDownloads.ThisRepositoryContainsThisEntryBut")
        };
        var body = new TextBlock { Text = summary + "\n\n" + string.Join("\n", installed.Take(5).Select(item => item.Path))
            + T("OnlineDownloads.ContinuingCreatesASeparateCopy"), TextWrapping = TextWrapping.Wrap, MaxWidth = 560 };
        var dialog = new ContentDialog { Title = T("OnlineDownloads.ExistingInstallationFound"), Content = body,
            PrimaryButtonText = T("OnlineDownloads.DownloadASeparateCopy"), CloseButtonText = T("Common.Cancel"),
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
        catch (Exception ex) { await ReportOnlineActionErrorAsync(ex.Message, T("OnlineDownloads.CannotRestartDownload")); }
        finally { task.RetryInProgress = false; if (!_onlineWindowClosed) RefreshDownloadTaskCard(task); }
    }
}

public sealed record OnlineDownloadRetryRequest(OnlineModCard Mod, OnlineDownloadCandidate? File,
    OnlineDownloadContext Context, bool EndfieldMetadata, IReadOnlyCollection<string> CharacterAliases, string Destination);
