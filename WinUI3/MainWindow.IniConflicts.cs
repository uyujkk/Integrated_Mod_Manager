using IntegratedModManager.Core;

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
            L("覆盖相同的游戏资源（同时启用时通常只有一个生效，或出现贴图、模型错乱）：",
                "Override the same game resources (when both are enabled, usually only one applies or textures and models break):"),
            iniConflicts.Where(item => item.Summary.Kind == ModIniConflictKind.ResourceHash)
                .Select(item => DescribeIniConflict(item.Candidate, item.Summary))
                .ToList());
        AddSection(
            L("使用相同的快捷键（按一次会同时触发）：", "Use the same shortcut (one press triggers both):"),
            iniConflicts.Where(item => item.Summary.Kind == ModIniConflictKind.Shortcut)
                .Select(item => DescribeIniConflict(item.Candidate, item.Summary))
                .ToList());
        AddSection(
            L("包含相同的相对文件路径（可能覆盖同一游戏资源）：",
                "Contain matching relative file paths (may override the same game resources):"),
            fileConflicts);

        return L("安装前检查发现以下潜在冲突。是否仍要继续？", "The pre-install check found these potential conflicts. Continue anyway?")
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
                body += Environment.NewLine + L($"……另有 {more} 项", $"...and {more} more");
            }

            sections.Add(title + Environment.NewLine + body);
        }
    }

    private string DescribeIniConflict(string? candidate, ModIniConflictSummary summary)
    {
        string pair = candidate is null ? summary.InstalledModName : $"{candidate} ↔ {summary.InstalledModName}";
        return summary.Kind == ModIniConflictKind.ResourceHash
            ? L($"{pair}：{summary.Count} 处相同哈希，例如 [{summary.InstalledSection}]（{summary.Example}）",
                $"{pair}: {summary.Count} shared hash(es), e.g. [{summary.InstalledSection}] ({summary.Example})")
            : L($"{pair}：{summary.Example}（[{summary.CandidateSection}] / [{summary.InstalledSection}]）",
                $"{pair}: {summary.Example} ([{summary.CandidateSection}] / [{summary.InstalledSection}])");
    }
}
