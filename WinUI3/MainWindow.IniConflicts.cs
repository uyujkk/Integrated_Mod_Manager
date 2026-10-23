using IntegratedModManager.Core.Deployment;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private const int ConflictLinesPerGroup = 8;

    // INI comparison is advisory: a failure is logged and never blocks deployment.
    private static IReadOnlyList<ModIniConflictSummary> DetectModIniConflictsSafely(
        string candidateDirectory,
        string targetRoot,
        IEnumerable<string> ignoredTargets)
    {
        try
        {
            return ModIniConflictDetector.FindConflictsInTarget(candidateDirectory, targetRoot, ignoredTargets);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            LogApplicationIssue("INI conflict detection", ex);
            return [];
        }
    }

    private string BuildInstallConflictText(
        IReadOnlyList<string> fileConflicts,
        IReadOnlyList<(string? Candidate, ModIniConflictSummary Summary)> iniConflicts)
    {
        var sections = new List<string>();
        AddSection(
            T("IniConflicts.OverrideTheSameGameResourcesWhen"),
            iniConflicts.Where(item => item.Summary.Kind == ModIniConflictKind.ResourceHash)
                .Select(item => DescribeIniConflict(item.Candidate, item.Summary))
                .ToList());
        AddSection(
            T("IniConflicts.UseTheSameShortcutOnePress"),
            iniConflicts.Where(item => item.Summary.Kind == ModIniConflictKind.Shortcut)
                .Select(item => DescribeIniConflict(item.Candidate, item.Summary))
                .ToList());
        AddSection(
            T("IniConflicts.ContainMatchingRelativeFilePathsMay"),
            fileConflicts);

        return T("IniConflicts.ThePreInstallCheckFoundThese")
            + Environment.NewLine + Environment.NewLine
            + string.Join(Environment.NewLine + Environment.NewLine, sections);

        void AddSection(string title, IReadOnlyList<string> lines)
        {
            if (lines.Count == 0)
            {
                return;
            }

            string body = string.Join(Environment.NewLine, lines.Take(ConflictLinesPerGroup).Select(line => "• " + line));
            if (lines.Count > ConflictLinesPerGroup)
            {
                int more = lines.Count - ConflictLinesPerGroup;
                body += Environment.NewLine + T("IniConflicts.AndMore", more);
            }

            sections.Add(title + Environment.NewLine + body);
        }
    }

    private string DescribeIniConflict(string? candidate, ModIniConflictSummary summary)
    {
        string pair = candidate is null ? summary.InstalledModName : $"{candidate} ↔ {summary.InstalledModName}";
        return summary.Kind == ModIniConflictKind.ResourceHash
            ? T("IniConflicts.SharedHashEsEG", pair, summary.Count, summary.InstalledSection, summary.Example)
            : T("IniConflicts.Text", pair, summary.Example, summary.CandidateSection, summary.InstalledSection);
    }
}
