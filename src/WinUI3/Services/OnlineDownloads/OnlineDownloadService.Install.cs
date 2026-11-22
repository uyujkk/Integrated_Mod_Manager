using IntegratedModManager.Core.Configuration;
using IntegratedModManager.Core.Online;
using IntegratedModManager.Core.Shortcuts;

namespace ModFolderCopier.WinUI;

// What an online install records: the tracking origin, shortcut bindings and links (moved from OnlinePage).
internal sealed partial class OnlineDownloadService
{
    internal sealed class PreparedOnlineInstallMetadata
    {
        public required TrackedModOrigin Origin { get; init; }
        public required List<ShortcutBinding> Bindings { get; init; }
        public Exception? PersistenceWarning { get; set; }
    }

    internal static OnlineDownloadContext CaptureOnlineDownloadContext(WorkspaceRepository? repository)
        => new(repository?.Id ?? string.Empty, repository?.SourcePath ?? string.Empty, repository?.Name ?? string.Empty);

    internal async Task<PreparedOnlineInstallMetadata> PrepareOnlineInstallMetadataAsync(
        string extractFolder, OnlineModCard mod, OnlineModDetails details, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? previewUrl = await SaveOnlinePreviewImageAsync(extractFolder, mod, details, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        List<ShortcutBinding> bindings = [];
        // Page-provided shortcuts retain priority. All work is staged: nothing is saved here.
        if (details.ShortcutBindings.Count > 0)
            bindings = await BuildLocalizedShortcutBindingsAsync(details.ShortcutBindings).WaitAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (bindings.Count == 0)
        {
            try
            {
                IReadOnlyList<DetectedModShortcut> detected = await Task.Run(
                    () => ModShortcutScanner.ScanDirectory(extractFolder, ShortcutBindingService.ScanSafetyLimit), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                bindings = detected.Take(ShortcutBindingService.ScanSafetyLimit).Select(_services.ShortcutBindings.CreateAutomatic).ToList();
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _host.LogApplicationIssue("Prepare online Mod shortcuts", ex); }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new PreparedOnlineInstallMetadata
        {
            Origin = new TrackedModOrigin
            {
                Path = extractFolder, SourceSite = OnlineSourceRules.DefaultSourceSite, ItemId = mod.ItemId,
                Title = mod.Title, ProfileUrl = mod.ProfileUrl,
                DownloadFileId = mod.DownloadFileId, DownloadFileName = mod.DownloadFileName,
                PreviewUrl = string.IsNullOrWhiteSpace(previewUrl) ? mod.PreviewUrl : previewUrl,
                LastKnownUpdatedAt = mod.UpdatedAt
            },
            Bindings = bindings.Take(ShortcutBindingService.ScanSafetyLimit).ToList()
        };
    }

    internal void CommitOnlineInstallMetadata(string extractFolder, PreparedOnlineInstallMetadata metadata)
    {
        // One UI-thread, non-awaiting commit. Keep other installations' records; a source ID
        // identifies the website entry, not a unique local installation.
        _services.ShortcutBindings.Links[extractFolder] = metadata.Origin.ProfileUrl;
        _services.Tracking.Origins[extractFolder] = metadata.Origin;
        if (metadata.Bindings.Count > 0)
            _services.ShortcutBindings.Bindings[extractFolder] = metadata.Bindings;
        if (!_host.SaveConfig())
            metadata.PersistenceWarning = new IOException("Installation tracking could not be persisted.");
    }

    internal void CleanupOnlineDownload(OnlineDownloadSession session, bool canceled)
    {
        OnlineDownloadCleanup cleanup = session.GetCleanup(canceled);
        if (cleanup.ArchivePath is not null)
        {
            try { if (File.Exists(cleanup.ArchivePath)) File.Delete(cleanup.ArchivePath); }
            catch (Exception ex) { _host.LogApplicationIssue("Owned online archive cleanup", ex); }
        }
        if (cleanup.ExtractionPath is not null)
        {
            try { if (Directory.Exists(cleanup.ExtractionPath)) _services.Install.DeleteDirectoryTreeSafely(cleanup.ExtractionPath); }
            catch (Exception ex) { _host.LogApplicationIssue("Owned online extraction cleanup", ex); }
        }
    }
}
