using System.Security;

namespace IntegratedModManager.Core;

public sealed record CombinationPresetState(string ModRelativePath, IReadOnlyList<ModPersistentValue> Values);
public sealed record CombinationRestoreRequest(
    string SourceRoot, string TargetRoot, IReadOnlyList<string> ModRelativePaths,
    bool UseLinks, bool IncludeState, IReadOnlyList<CombinationPresetState> States, string UserIniPath);

public enum CombinationPreviewIssueCode
{
    SourceUnavailable, TargetUnavailable, UnsafeRoots, UnsafeModPath, MissingMod,
    DisabledMod, LinkedSource, DuplicateMod, AmbiguousTargetName, SnapshotMismatch,
    ParameterMismatch, LoaderStateInvalid, DeploymentBlocked, ReadFailure
}
public sealed record CombinationPreviewIssue(CombinationPreviewIssueCode Code, string Subject, Exception? Error = null);
public enum CombinationModAction { Install, Keep, Remove, PreserveExternal, Unavailable, Pending }
public sealed record CombinationModPreview(string ModRelativePath, string Path, CombinationModAction Action);
public sealed record CombinationParameterPreview(string ModRelativePath, ModPersistentValueChange Change);
public sealed record CombinationRestorePreview(
    ModCombinationDeploymentPlan? Deployment, ModPersistentRuntimeRestorePlan? Runtime,
    IReadOnlyList<CombinationModPreview> Mods, IReadOnlyList<CombinationParameterPreview> Parameters,
    IReadOnlyList<CombinationPreviewIssue> Issues)
{
    public bool CanRestore => Deployment is not null && Issues.Count == 0;
}

/// <summary>
/// Read-only preparation. Existing strict planners remain the final authority.
/// Missing/ambiguous items block the whole restore: never guess a replacement,
/// omit saved state, rename a Mod, repair an INI or mutate deployment here.
/// </summary>
public static class CombinationRestorePreviewService
{
    public static CombinationRestorePreview Prepare(CombinationRestoreRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        List<CombinationPreviewIssue> issues = [];
        List<CombinationModPreview> mods = [];
        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var stateSources = new List<(string Relative, ModPersistentSourceState Source)>();
        ModCombinationDeploymentPlan? deployment = null;
        ModPersistentRuntimeRestorePlan? runtime = null;
        List<CombinationParameterPreview> parameters = [];

        CombinationRestorePreview Result() => new(deployment, runtime, mods.ToArray(), parameters.ToArray(), issues.ToArray());
        if (request.ModRelativePaths is null || request.States is null)
        {
            issues.Add(new(CombinationPreviewIssueCode.SnapshotMismatch, string.Empty));
            return Result();
        }
        string sourceRoot, targetRoot;
        try
        {
            sourceRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.SourceRoot));
            targetRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.TargetRoot));
            if (sourceRoot.Equals(targetRoot, StringComparison.OrdinalIgnoreCase)
                || IsInside(sourceRoot, targetRoot) || IsInside(targetRoot, sourceRoot))
                issues.Add(new(CombinationPreviewIssueCode.UnsafeRoots, string.Empty));
            CheckRoot(sourceRoot, CombinationPreviewIssueCode.SourceUnavailable);
            CheckRoot(targetRoot, CombinationPreviewIssueCode.TargetUnavailable);
        }
        catch (Exception ex) when (IsPreflightError(ex))
        {
            issues.Add(new(CombinationPreviewIssueCode.UnsafeRoots, string.Empty, ex));
            return Result();
        }
        if (issues.Count > 0) return Result();

        void CheckRoot(string root, CombinationPreviewIssueCode code)
        {
            if (!Directory.Exists(root)) issues.Add(new(code, root));
            else if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                issues.Add(new(CombinationPreviewIssueCode.UnsafeRoots, root));
        }

        foreach (string relative in request.ModRelativePaths)
        {
            string? normalized = NormalizeModPath(relative);
            if (normalized is null)
            {
                issues.Add(new(CombinationPreviewIssueCode.UnsafeModPath, relative ?? string.Empty));
                mods.Add(new(relative ?? string.Empty, string.Empty, CombinationModAction.Unavailable));
                continue;
            }
            string path = Path.Combine(sourceRoot, normalized);
            if (!sources.TryAdd(normalized, path))
            {
                issues.Add(new(CombinationPreviewIssueCode.DuplicateMod, normalized));
                continue;
            }
            var code = InspectMod(sourceRoot, path, normalized);
            if (code.HasValue) issues.Add(new(code.Value, normalized));
            mods.Add(new(normalized, path, code.HasValue ? CombinationModAction.Unavailable : CombinationModAction.Pending));
        }
        foreach (var group in sources.Keys.GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            issues.Add(new(CombinationPreviewIssueCode.AmbiguousTargetName, string.Join(" · ", group)));

        if (request.IncludeState)
        {
            var statePaths = request.States.Select(state => state is null ? null : NormalizeModPath(state.ModRelativePath)).ToArray();
            if (statePaths.Any(path => path is null) || statePaths.Length != sources.Count
                || statePaths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != statePaths.Length
                || !sources.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(statePaths.Select(path => path!)))
                issues.Add(new(CombinationPreviewIssueCode.SnapshotMismatch, string.Empty));
            else
            {
                for (int i = 0; i < request.States.Count; i++)
                {
                    string relative = statePaths[i]!;
                    if (mods.Any(mod => mod.ModRelativePath.Equals(relative, StringComparison.OrdinalIgnoreCase) && mod.Action == CombinationModAction.Unavailable)) continue;
                    var source = new ModPersistentSourceState(sources[relative], Path.GetFileName(relative), request.States[i].Values);
                    try
                    {
                        ModPersistentPresetEngine.ValidateSourceState(source);
                        stateSources.Add((relative, source));
                    }
                    catch (Exception ex) when (IsPreflightError(ex))
                    {
                        issues.Add(new(CombinationPreviewIssueCode.ParameterMismatch, relative, ex));
                    }
                }
            }
        }

        // Only strict valid preflight may produce an executable plan.
        if (issues.Count > 0) return Result();
        try
        {
            deployment = ModCombinationDeploymentPolicy.Plan(sourceRoot, targetRoot, sources.Keys.ToArray(), request.UseLinks);
            var install = deployment.InstallSources.ToHashSet(StringComparer.OrdinalIgnoreCase);
            mods = deployment.DesiredSources.Select(path => new CombinationModPreview(
                Path.GetRelativePath(sourceRoot, path), path,
                install.Contains(path) ? CombinationModAction.Install : CombinationModAction.Keep)).ToList();
            mods.AddRange(deployment.RemoveTargets.Select(path => new CombinationModPreview(
                Path.GetFileName(path), path, CombinationModAction.Remove)));
            var enabledTargets = ModCombinationDeploymentPolicy.Capture(sourceRoot, targetRoot)
                .Select(relative => Path.GetFileName(relative)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            mods.AddRange(Directory.GetDirectories(targetRoot)
                .Where(path => !enabledTargets.Contains(Path.GetFileName(path)))
                .OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase)
                .Select(path => new CombinationModPreview(Path.GetFileName(path), path, CombinationModAction.PreserveExternal)));
            // Copies may have different declarations from their repository source.
            // Check already deployed copies before any deployment transaction.
            if (!request.UseLinks)
                foreach (var item in stateSources.Where(item => !install.Contains(item.Source.ModDirectory)))
                {
                    try
                    {
                        ModPersistentPresetEngine.ValidateSourceState(item.Source with
                        { ModDirectory = Path.Combine(targetRoot, item.Source.DeployedRelativePath) });
                    }
                    catch (Exception ex) when (IsPreflightError(ex))
                    { issues.Add(new(CombinationPreviewIssueCode.ParameterMismatch, item.Relative, ex)); }
                }
        }
        catch (Exception ex) when (IsPreflightError(ex))
        {
            deployment = null;
            issues.Add(new(CombinationPreviewIssueCode.DeploymentBlocked, string.Empty, ex));
        }
        if (request.IncludeState && stateSources.Any(item => item.Source.Values.Count > 0))
        {
            try
            {
                runtime = ModPersistentPresetEngine.PlanCombinationRuntimeRestore(stateSources.Select(item => item.Source).ToArray(), request.UserIniPath);
                var owners = stateSources.SelectMany(item => item.Source.Values.Select(value =>
                    (Key: NormalizeKey(value.RuntimeKey), item.Relative)))
                    .ToDictionary(item => item.Key, item => item.Relative, StringComparer.OrdinalIgnoreCase);
                parameters.AddRange(runtime.Changes.Select(change => new CombinationParameterPreview(owners[NormalizeKey(change.Saved.RuntimeKey)], change)));
            }
            catch (Exception ex) when (IsPreflightError(ex))
            {
                runtime = null;
                issues.Add(new(CombinationPreviewIssueCode.LoaderStateInvalid, string.Empty, ex));
            }
        }
        return Result();
    }

    // Called after all confirmation dialogs and immediately before backups.
    // Declaration validation happens again in Prepare; apply still checks file
    // bytes and validates declarations. This is not an OS/game process lock.
    public static bool IsStillApproved(CombinationRestorePreview approved, CombinationRestorePreview fresh)
    {
        if (!approved.CanRestore || !fresh.CanRestore) return false;
        var left = approved.Deployment!;
        var right = fresh.Deployment!;
        if (!left.DesiredSources.SequenceEqual(right.DesiredSources, StringComparer.OrdinalIgnoreCase)
            || !left.InstallSources.SequenceEqual(right.InstallSources, StringComparer.OrdinalIgnoreCase)
            || !left.RemoveTargets.SequenceEqual(right.RemoveTargets, StringComparer.OrdinalIgnoreCase)) return false;
        if (!approved.Mods.SequenceEqual(fresh.Mods)) return false;
        if (approved.Runtime is null || fresh.Runtime is null) return approved.Runtime is null && fresh.Runtime is null;
        return approved.Runtime.Edit.Path.Equals(fresh.Runtime.Edit.Path, StringComparison.OrdinalIgnoreCase)
            && approved.Runtime.Edit.OriginalBytes.SequenceEqual(fresh.Runtime.Edit.OriginalBytes)
            && approved.Runtime.Edit.UpdatedBytes.SequenceEqual(fresh.Runtime.Edit.UpdatedBytes);
    }

    private static CombinationPreviewIssueCode? InspectMod(string sourceRoot, string path, string relative)
    {
        if (relative.Split('\\').Any(part => part.StartsWith("DISABLED", StringComparison.OrdinalIgnoreCase)))
            return CombinationPreviewIssueCode.DisabledMod;
        try
        {
            string category = Path.GetDirectoryName(path)!;
            if ((Directory.Exists(category) && (File.GetAttributes(category) & FileAttributes.ReparsePoint) != 0)
                || (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0))
                return CombinationPreviewIssueCode.LinkedSource;
            if (!IsInside(sourceRoot, path) || !Directory.Exists(path)) return CombinationPreviewIssueCode.MissingMod;
            return null;
        }
        catch (Exception ex) when (IsPreflightError(ex)) { return CombinationPreviewIssueCode.ReadFailure; }
    }

    private static string? NormalizeModPath(string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) return null;
        string[] parts = relative.Replace('/', '\\').Split('\\');
        return parts.Length == 2 && parts.All(part => !string.IsNullOrWhiteSpace(part)
            && part is not "." and not ".." && !part.EndsWith('.') && !part.EndsWith(' ')
            && part.IndexOfAny(Path.GetInvalidFileNameChars()) < 0) ? string.Join('\\', parts) : null;
    }
    private static bool IsInside(string root, string path) => path.StartsWith(
        root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar,
        StringComparison.OrdinalIgnoreCase);
    private static string NormalizeKey(string key) => key.Trim().Replace('/', '\\').ToLowerInvariant();
    private static bool IsPreflightError(Exception ex)
        => ex is IOException or InvalidDataException or UnauthorizedAccessException or SecurityException or ArgumentException;
}
