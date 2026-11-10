using IntegratedModManager.Core.Configuration;
using IntegratedModManager.Core.Online;
using IntegratedModManager.Core.Shortcuts;

namespace ModFolderCopier.WinUI;

public sealed partial class OnlinePage
{
    private sealed class PreparedOnlineInstallMetadata
    {
        public required TrackedModOrigin Origin { get; init; }
        public required List<ShortcutBinding> Bindings { get; init; }
        public Exception? PersistenceWarning { get; set; }
    }

    internal static OnlineDownloadContext CaptureOnlineDownloadContext(WorkspaceRepository? repository)
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
                    () => ModShortcutScanner.ScanDirectory(extractFolder, _host.ShortcutScanSafetyLimit), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                bindings = detected.Take(_host.ShortcutScanSafetyLimit).Select(_host.CreateAutomaticShortcutBinding).ToList();
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
            Bindings = bindings.Take(_host.ShortcutScanSafetyLimit).ToList()
        };
    }

    private void CommitOnlineInstallMetadata(string extractFolder, PreparedOnlineInstallMetadata metadata)
    {
        // One UI-thread, non-awaiting commit. Keep other installations' records; a source ID
        // identifies the website entry, not a unique local installation.
        _host.ModLinks[extractFolder] = metadata.Origin.ProfileUrl;
        _host.TrackedModOrigins[extractFolder] = metadata.Origin;
        if (metadata.Bindings.Count > 0)
            _host.ModBindings[extractFolder] = metadata.Bindings;
        if (!_host.SaveConfig())
            metadata.PersistenceWarning = new IOException("Installation tracking could not be persisted.");
    }

    private void CleanupOnlineDownload(OnlineDownloadSession session, bool canceled)
    {
        OnlineDownloadCleanup cleanup = session.GetCleanup(canceled);
        if (cleanup.ArchivePath is not null)
        {
            try { if (File.Exists(cleanup.ArchivePath)) File.Delete(cleanup.ArchivePath); }
            catch (Exception ex) { _host.LogApplicationIssue("Owned online archive cleanup", ex); }
        }
        if (cleanup.ExtractionPath is not null)
        {
            try { if (Directory.Exists(cleanup.ExtractionPath)) _host.DeleteDirectoryTreeSafely(cleanup.ExtractionPath); }
            catch (Exception ex) { _host.LogApplicationIssue("Owned online extraction cleanup", ex); }
        }
    }
}
