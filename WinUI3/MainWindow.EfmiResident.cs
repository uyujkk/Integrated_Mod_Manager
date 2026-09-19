using IntegratedModManager.Core;
using Microsoft.UI.Xaml;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private async void OnAnalyzeEfmiResidentClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            SetEfmiResidentBusy(true);
            (List<EfmiResidentModDefinition> mods, List<EfmiResidentProfile> profiles) = BuildEfmiResidentModel();
            EfmiResidentStatusTextBlock.Text = L("正在分析受控 Mod…", "Analyzing managed mods...");
            List<(EfmiResidentModDefinition Mod, EfmiResidentAnalysis Analysis)> results = await Task.Run(() =>
                mods.Select(mod => (mod, EfmiResidentMod.Analyze(mod.SourcePath))).ToList());

            int supported = results.Count(result => result.Analysis.Compatibility == EfmiResidentCompatibility.Supported);
            int review = results.Count(result => result.Analysis.Compatibility == EfmiResidentCompatibility.ReviewRequired);
            int unsupported = results.Count(result => result.Analysis.Compatibility == EfmiResidentCompatibility.Unsupported);
            string details = string.Join(Environment.NewLine, results.Select(result =>
            {
                string issue = result.Analysis.Issues.FirstOrDefault()?.Message ?? L("可安全生成。", "Safe to generate.");
                return $"• {result.Mod.DisplayName}: {GetCompatibilityText(result.Analysis.Compatibility)} — {issue}";
            }));
            EfmiResidentStatusTextBlock.Text = L(
                $"分析完成：{profiles.Count} 个方案、{mods.Count} 个 Mod；支持 {supported}，需检查 {review}，不支持 {unsupported}。",
                $"Analysis complete: {profiles.Count} profiles, {mods.Count} mods; supported {supported}, review {review}, unsupported {unsupported}.")
                + Environment.NewLine + details;
        }
        catch (Exception ex)
        {
            EfmiResidentStatusTextBlock.Text = L("分析失败：", "Analysis failed: ") + ex.Message;
            await ShowMessageAsync(
                L("无法分析 EFMI 常驻方案：", "Could not analyze the EFMI resident set: ") + ex.Message,
                L("分析失败", "Analysis Failed"));
        }
        finally
        {
            SetEfmiResidentBusy(false);
        }
    }

    private async void OnGenerateEfmiResidentClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            (List<EfmiResidentModDefinition> mods, List<EfmiResidentProfile> profiles) = BuildEfmiResidentModel();
            string? parent = await PickFolderAsync();
            if (string.IsNullOrWhiteSpace(parent))
            {
                return;
            }

            string output = Path.Combine(parent, "IntegratedModManager Resident Set");
            if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            {
                await ShowMessageAsync(
                    L(
                        "输出子文件夹已存在且不为空。为保护现有文件，软件不会覆盖它：\n" + output,
                        "The output subfolder exists and is not empty. It will not be overwritten:\n" + output),
                    L("请选择其他位置", "Choose Another Location"));
                return;
            }

            SetEfmiResidentBusy(true);
            EfmiResidentStatusTextBlock.Text = L("正在重新验证并生成受控副本…", "Revalidating and generating managed copies...");
            EfmiResidentDeploymentResult result = await Task.Run(() =>
                EfmiResidentMod.GenerateDeployment(output, mods, profiles));
            EfmiResidentStatusTextBlock.Text = L(
                $"已生成：{result.GeneratedModPaths.Count} 个常驻 Mod、{profiles.Count} 个游戏内方案。尚未写入 EFMI。",
                $"Generated {result.GeneratedModPaths.Count} resident mods and {profiles.Count} in-game profiles. EFMI was not modified.")
                + Environment.NewLine + result.OutputPath;
            await ShowMessageAsync(
                L(
                    "常驻方案已生成到独立文件夹。原始 Mod 和 EFMI 安装目录均未修改。\n\n" + result.OutputPath,
                    "The resident set was generated in an independent folder. Source mods and the EFMI installation were not modified.\n\n" + result.OutputPath),
                L("生成完成", "Generation Complete"));
        }
        catch (InvalidDataException ex)
        {
            EfmiResidentStatusTextBlock.Text = L("安全检查未通过：", "Safety validation failed: ") + ex.Message;
            await ShowMessageAsync(
                L("存在无法自动门控的 Mod，已停止生成：\n", "A mod cannot be gated safely, so generation was stopped:\n") + ex.Message,
                L("兼容性检查未通过", "Compatibility Check Failed"));
        }
        catch (Exception ex)
        {
            EfmiResidentStatusTextBlock.Text = L("生成失败：", "Generation failed: ") + ex.Message;
            await ShowMessageAsync(
                L("生成 EFMI 常驻方案失败：", "Failed to generate the EFMI resident set: ") + ex.Message,
                L("生成失败", "Generation Failed"));
        }
        finally
        {
            SetEfmiResidentBusy(false);
        }
    }

    private (List<EfmiResidentModDefinition> Mods, List<EfmiResidentProfile> Profiles) BuildEfmiResidentModel()
    {
        WorkspaceRepository repository = GetSelectedRepository()
            ?? throw new InvalidOperationException(L("请先选择一个仓库。", "Select a repository first."));
        if (!Directory.Exists(repository.SourcePath))
        {
            throw new DirectoryNotFoundException(L("仓库源文件夹不存在。", "The repository source folder does not exist."));
        }

        List<ModConfigurationProfile> sourceProfiles = _configurationProfiles
            .Where(profile => string.Equals(profile.RepositoryId, repository.Id, StringComparison.Ordinal))
            .OrderBy(profile => profile.UpdatedAtUtc)
            .ToList();
        if (sourceProfiles.Count == 0)
        {
            throw new InvalidOperationException(L(
                "当前仓库还没有配置方案。请先在上方建立至少一个配置方案。",
                "This repository has no configuration profile. Create at least one profile above first."));
        }

        Dictionary<string, EfmiResidentModDefinition> byRelativePath = new(StringComparer.OrdinalIgnoreCase);
        foreach (ModConfigurationProfile profile in sourceProfiles)
        {
            foreach (string relativePath in profile.ModRelativePaths)
            {
                string sourcePath = PathSafety.ResolveInsideDirectory(repository.SourcePath, relativePath);
                if (!Directory.Exists(sourcePath))
                {
                    throw new DirectoryNotFoundException(L(
                        $"方案“{profile.Name}”引用的 Mod 不存在：{relativePath}",
                        $"Profile '{profile.Name}' references a missing mod: {relativePath}"));
                }

                byRelativePath.TryAdd(relativePath, new(
                    EfmiResidentMod.CreateStableModId(Path.GetFileName(sourcePath), sourcePath),
                    Path.GetFileName(sourcePath),
                    sourcePath));
            }
        }

        if (byRelativePath.Count == 0)
        {
            throw new InvalidOperationException(L("配置方案中没有 Mod。", "The configuration profiles contain no mods."));
        }

        List<EfmiResidentModDefinition> mods = byRelativePath.Values
            .OrderBy(mod => mod.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        List<EfmiResidentProfile> profiles = [];
        for (int index = 0; index < sourceProfiles.Count; index++)
        {
            ModConfigurationProfile sourceProfile = sourceProfiles[index];
            HashSet<string> enabledIds = sourceProfile.ModRelativePaths
                .Where(byRelativePath.ContainsKey)
                .Select(path => byRelativePath[path].Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            profiles.Add(new(index + 1, sourceProfile.Name, enabledIds));
        }

        return (mods, profiles);
    }

    private string GetCompatibilityText(EfmiResidentCompatibility compatibility) => compatibility switch
    {
        EfmiResidentCompatibility.Supported => L("支持", "Supported"),
        EfmiResidentCompatibility.ReviewRequired => L("需要人工检查", "Review required"),
        _ => L("不支持", "Unsupported")
    };

    private void SetEfmiResidentBusy(bool busy)
    {
        AnalyzeEfmiResidentButton.IsEnabled = !busy;
        GenerateEfmiResidentButton.IsEnabled = !busy;
    }
}
