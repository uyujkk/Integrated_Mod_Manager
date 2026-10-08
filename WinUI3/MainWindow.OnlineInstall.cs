using IntegratedModManager.Core;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private sealed class PreparedOnlineInstallMetadata
    {
        public required TrackedModOrigin Origin { get; init; }
        public required List<ShortcutBinding> Bindings { get; init; }
        public Exception? PersistenceWarning { get; set; }
    }

    private static OnlineDownloadContext CaptureOnlineDownloadContext(WorkspaceRepository? repository)
        => new(repository?.Id ?? string.Empty, repository?.SourcePath ?? string.Empty, repository?.Name ?? string.Empty);

    private async Task<PreparedOnlineInstallMetadata> PrepareOnlineInstallMetadataAsync(
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
                    () => ModShortcutScanner.ScanDirectory(extractFolder, ShortcutScanSafetyLimit), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                bindings = detected.Take(ShortcutScanSafetyLimit).Select(CreateAutomaticShortcutBinding).ToList();
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { LogApplicationIssue("Prepare online Mod shortcuts", ex); }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new PreparedOnlineInstallMetadata
        {
            Origin = new TrackedModOrigin
            {
                Path = extractFolder, SourceSite = DefaultOnlineSourceSite, ItemId = mod.ItemId,
                Title = mod.Title, ProfileUrl = mod.ProfileUrl,
                PreviewUrl = string.IsNullOrWhiteSpace(previewUrl) ? mod.PreviewUrl : previewUrl,
                LastKnownUpdatedAt = mod.UpdatedAt
            },
            Bindings = bindings.Take(ShortcutScanSafetyLimit).ToList()
        };
    }

    private void CommitOnlineInstallMetadata(string extractFolder, PreparedOnlineInstallMetadata metadata)
    {
        // One UI-thread, non-awaiting commit. Keep other installations' records; a source ID
        // identifies the website entry, not a unique local installation.
        _modLinks[extractFolder] = metadata.Origin.ProfileUrl;
        _trackedModOrigins[extractFolder] = metadata.Origin;
        if (metadata.Bindings.Count > 0)
            _modBindings[extractFolder] = metadata.Bindings;
        if (!SaveConfig())
            metadata.PersistenceWarning = new IOException("Installation tracking could not be persisted.");
    }

    private void CleanupOnlineDownload(OnlineDownloadSession session, bool canceled)
    {
        OnlineDownloadCleanup cleanup = session.GetCleanup(canceled);
        if (cleanup.ArchivePath is not null)
        {
            try { if (File.Exists(cleanup.ArchivePath)) File.Delete(cleanup.ArchivePath); }
            catch (Exception ex) { LogApplicationIssue("Owned online archive cleanup", ex); }
        }
        if (cleanup.ExtractionPath is not null)
        {
            try { if (Directory.Exists(cleanup.ExtractionPath)) DeleteDirectoryTreeSafely(cleanup.ExtractionPath); }
            catch (Exception ex) { LogApplicationIssue("Owned online extraction cleanup", ex); }
        }
    }
}
