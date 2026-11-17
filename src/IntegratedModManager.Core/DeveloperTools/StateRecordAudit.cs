using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Infrastructure;

namespace IntegratedModManager.Core.DeveloperTools;

/// <summary>A repository as the audit needs it (from <c>beta-shell.json</c>).</summary>
public sealed record AuditRepository(string Id, string Name, string SourcePath, string TargetPath);

/// <summary>A saved state slot as the audit needs it.</summary>
public sealed record AuditSlot(string RepositoryId, string ModRelativePath, string Name, string? PreviewFile);

public enum AuditFindingKind
{
    /// <summary>A copy record names a repository that no longer exists.</summary>
    RecordForUnknownRepository,
    /// <summary>The folder a copy record says was copied into the target is gone.</summary>
    RecordFolderMissing,
    /// <summary>The source folder a copy record was made from is gone.</summary>
    RecordSourceMissing,
    /// <summary>A copy record points outside its repository's target folder.</summary>
    RecordOutsideTarget,
    /// <summary>A state slot names a repository that no longer exists.</summary>
    SlotForUnknownRepository,
    /// <summary>The Mod a state slot belongs to is no longer in the repository.</summary>
    SlotModMissing,
    /// <summary>A state slot's picture file is gone.</summary>
    SlotPreviewMissing,
    /// <summary>A directory link in a target folder whose destination is gone.</summary>
    BrokenLink
}

/// <summary>One thing the audit found. <see cref="Path"/> is the folder or file to look at.</summary>
public sealed record AuditFinding(AuditFindingKind Kind, string RepositoryId, string Path, string Detail);

public sealed record AuditResult(int Records, int Slots, int Links, IReadOnlyList<AuditFinding> Findings);

/// <summary>
/// Read-only checks of the records the app keeps about Mod folders (copy records since #107, state slots, directory
/// links in target folders). Nothing is changed; the result says what to look at.
/// </summary>
public static class StateRecordAudit
{
    public static AuditResult Run(
        IReadOnlyList<AuditRepository> repositories,
        IReadOnlyList<CopyDeploymentRecord> records,
        IReadOnlyList<AuditSlot> slots,
        string? previewFolder)
    {
        ArgumentNullException.ThrowIfNull(repositories);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(slots);
        var byId = repositories
            .Where(repository => !string.IsNullOrWhiteSpace(repository.Id))
            .GroupBy(repository => repository.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var findings = new List<AuditFinding>();

        foreach (CopyDeploymentRecord record in records)
        {
            if (!byId.TryGetValue(record.RepositoryId, out AuditRepository? repository))
            {
                findings.Add(new(AuditFindingKind.RecordForUnknownRepository, record.RepositoryId, record.DeployedPath, record.SourceRelativePath));
                continue;
            }
            if (!string.IsNullOrWhiteSpace(repository.TargetPath) && !string.IsNullOrWhiteSpace(record.DeployedPath)
                && !PathSafety.IsStrictlyInside(record.DeployedPath, repository.TargetPath))
                findings.Add(new(AuditFindingKind.RecordOutsideTarget, repository.Id, record.DeployedPath, repository.TargetPath));
            else if (!Directory.Exists(record.DeployedPath))
                findings.Add(new(AuditFindingKind.RecordFolderMissing, repository.Id, record.DeployedPath, record.SourceRelativePath));
            string? source = Resolve(repository.SourcePath, record.SourceRelativePath);
            if (source is not null && !Directory.Exists(source))
                findings.Add(new(AuditFindingKind.RecordSourceMissing, repository.Id, source, record.DeployedPath));
        }

        foreach (AuditSlot slot in slots)
        {
            if (!byId.TryGetValue(slot.RepositoryId, out AuditRepository? repository))
            {
                findings.Add(new(AuditFindingKind.SlotForUnknownRepository, slot.RepositoryId, slot.ModRelativePath, slot.Name));
                continue;
            }
            string? mod = Resolve(repository.SourcePath, slot.ModRelativePath);
            if (mod is not null && !Directory.Exists(mod))
                findings.Add(new(AuditFindingKind.SlotModMissing, repository.Id, mod, slot.Name));
            if (!string.IsNullOrWhiteSpace(slot.PreviewFile) && !string.IsNullOrWhiteSpace(previewFolder))
            {
                string? preview = Resolve(previewFolder, slot.PreviewFile);
                if (preview is not null && !File.Exists(preview))
                    findings.Add(new(AuditFindingKind.SlotPreviewMissing, repository.Id, preview, slot.Name));
            }
        }

        int links = 0;
        foreach (AuditRepository repository in byId.Values)
        {
            if (string.IsNullOrWhiteSpace(repository.TargetPath) || !Directory.Exists(repository.TargetPath)) continue;
            foreach (string folder in SafeEnumerate(repository.TargetPath))
            {
                if (!DirectoryLinkDeployment.IsDirectoryLink(folder)) continue;
                links++;
                if (DirectoryLinkDeployment.IsBrokenLink(folder))
                    findings.Add(new(AuditFindingKind.BrokenLink, repository.Id, folder, DirectoryLinkDeployment.TryGetStoredTarget(folder) ?? string.Empty));
            }
        }

        return new AuditResult(records.Count, slots.Count, links, findings);
    }

    // A stored relative path inside a root; null when it would leave the root or is empty.
    private static string? Resolve(string root, string? relative)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(relative)) return null;
        try { return PathSafety.ResolveInsideDirectory(root, relative); }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidDataException or InvalidOperationException or NotSupportedException) { return null; }
    }

    private static IEnumerable<string> SafeEnumerate(string folder)
    {
        try { return Directory.EnumerateDirectories(folder).ToArray(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }
}
