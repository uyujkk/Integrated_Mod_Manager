using IntegratedModManager.Core.Configuration;
using IntegratedModManager.Core.Shortcuts;

namespace ModFolderCopier.WinUI;

// Shortcut binding rules the Mod workspace's editor and the online install share: reading bindings from a Mod's files,
// the automatic binding and its description, upgrading old automatic bindings and link normalising (split from
// MainWindow.ShortcutBindings.cs in stage J7; the editor itself moves with the workspace page).
public sealed partial class MainWindow
{
    private async Task<int> TryImportShortcutBindingsFromModFilesAsync(string modFolder)
    {
        if (string.IsNullOrWhiteSpace(modFolder)
            || !Directory.Exists(modFolder))
        {
            return 0;
        }

        bool hasExistingBindings = _modBindings.TryGetValue(modFolder, out List<ShortcutBinding>? existing)
            && existing.Any(binding => !string.IsNullOrWhiteSpace(binding.Shortcut)
                || !string.IsNullOrWhiteSpace(binding.Action));
        bool mayContainLegacyAutomaticDescriptions = hasExistingBindings
            && existing!.Any(binding => IsPossibleLegacyAutomaticShortcutDescription(binding.Action));
        if (hasExistingBindings && !mayContainLegacyAutomaticDescriptions)
        {
            return 0;
        }

        IReadOnlyList<DetectedModShortcut> detected;
        try
        {
            detected = await Task.Run(() => ModShortcutScanner.ScanDirectory(modFolder, ShortcutScanSafetyLimit));
        }
        catch (Exception ex)
        {
            LogApplicationIssue("Read Mod shortcut INI", ex);
            return 0;
        }

        if (detected.Count == 0)
        {
            return 0;
        }

        if (hasExistingBindings)
        {
            int upgradedCount = UpgradeLegacyAutomaticShortcutBindings(existing!, detected);
            if (upgradedCount > 0)
            {
                SaveConfig();
            }

            return upgradedCount;
        }

        _modBindings[modFolder] = detected
            .Take(ShortcutScanSafetyLimit)
            .Select(CreateAutomaticShortcutBinding)
            .ToList();
        SaveConfig();
        return _modBindings[modFolder].Count;
    }

    private string DescribeDetectedShortcut(DetectedModShortcut shortcut)
    {
        return ModShortcutDescriptionFormatter.Describe(
            shortcut,
            useEnglish: !IsChineseUi);
    }

    private ShortcutBinding CreateAutomaticShortcutBinding(DetectedModShortcut shortcut)
    {
        return new ShortcutBinding(shortcut.Shortcut, DescribeDetectedShortcut(shortcut))
        {
            AutoSection = shortcut.Section,
            AutoBehavior = shortcut.Behavior,
            AutoTarget = shortcut.Target,
            AutoSourceFile = shortcut.SourceFile
        };
    }

    private string GetShortcutBindingDisplayAction(ShortcutBinding binding)
    {
        return binding.HasAutoDescription
            ? DescribeDetectedShortcut(ToDetectedModShortcut(binding))
            : binding.Action;
    }

    private static DetectedModShortcut ToDetectedModShortcut(ShortcutBinding binding) =>
        new(
            binding.Shortcut,
            binding.AutoSection,
            binding.AutoBehavior,
            binding.AutoTarget,
            binding.AutoSourceFile);

    private static bool IsPossibleLegacyAutomaticShortcutDescription(string? action)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            return false;
        }

        string value = action.Trim();
        return value.StartsWith("循环切换 ", StringComparison.Ordinal)
            || value.StartsWith("按住触发 ", StringComparison.Ordinal)
            || value.StartsWith("切换 ", StringComparison.Ordinal)
            || value.StartsWith("触发 ", StringComparison.Ordinal)
            || value.StartsWith("Cycle ", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Hold ", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Toggle ", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Trigger ", StringComparison.OrdinalIgnoreCase);
    }

    private int UpgradeLegacyAutomaticShortcutBindings(
        List<ShortcutBinding> existingBindings,
        IReadOnlyList<DetectedModShortcut> detectedShortcuts)
    {
        int upgradedCount = 0;
        for (int index = 0; index < existingBindings.Count; index++)
        {
            ShortcutBinding existingBinding = existingBindings[index];
            if (existingBinding.HasAutoDescription
                || !IsPossibleLegacyAutomaticShortcutDescription(existingBinding.Action))
            {
                continue;
            }

            DetectedModShortcut? match = detectedShortcuts.FirstOrDefault(shortcut =>
                string.Equals(shortcut.Shortcut, existingBinding.Shortcut, StringComparison.OrdinalIgnoreCase)
                && ModShortcutDescriptionFormatter.MatchesLegacyDescription(existingBinding.Action, shortcut));
            if (match is null)
            {
                continue;
            }

            existingBindings[index] = CreateAutomaticShortcutBinding(match);
            upgradedCount++;
        }

        return upgradedCount;
    }

    private static string NormalizeLink(string link)
    {
        string trimmed = link.Trim();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return "https://" + trimmed;
    }
}
