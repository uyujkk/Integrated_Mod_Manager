using IntegratedModManager.Core.Configuration;
using IntegratedModManager.Core.Shortcuts;

namespace ModFolderCopier.WinUI;

/// <summary>
/// The Mods' shortcut bindings and links (config.ini) and the rules the workspace's editor and the online install share
/// (stage J finish, step 8b, moved from the window unchanged): reading bindings from a Mod's files, the automatic
/// binding and its description, and upgrading automatic bindings an older version wrote.
/// </summary>
internal sealed class ShortcutBindingService(AppServices services)
{
    /// <summary>The most bindings read from one Mod.</summary>
    public const int ScanSafetyLimit = 256;

    private readonly AppServices _services = services;
    private Action<string, Exception> _log = (_, _) => { };
    private Func<bool> _saveConfig = () => true;

    /// <summary>Each Mod's bindings, by its folder.</summary>
    public Dictionary<string, List<ShortcutBinding>> Bindings { get; } = new(StringComparer.CurrentCultureIgnoreCase);

    /// <summary>Each Mod's link, by its folder.</summary>
    public Dictionary<string, string> Links { get; } = new(StringComparer.CurrentCultureIgnoreCase);

    /// <summary>Called once by the window: the error log and saving config.ini.</summary>
    public void Attach(Action<string, Exception> log, Func<bool> saveConfig)
    {
        _log = log;
        _saveConfig = saveConfig;
    }

    /// <summary>
    /// Reads a Mod's bindings from its INI files when it has none yet, or upgrades automatic descriptions an older
    /// version wrote; saves config.ini when anything changed. Returns the number of bindings added or upgraded.
    /// </summary>
    public async Task<int> TryImportFromModFilesAsync(string modFolder)
    {
        if (string.IsNullOrWhiteSpace(modFolder)
            || !Directory.Exists(modFolder))
        {
            return 0;
        }

        bool hasExistingBindings = Bindings.TryGetValue(modFolder, out List<ShortcutBinding>? existing)
            && existing.Any(binding => !string.IsNullOrWhiteSpace(binding.Shortcut)
                || !string.IsNullOrWhiteSpace(binding.Action));
        bool mayContainLegacyAutomaticDescriptions = hasExistingBindings
            && existing!.Any(binding => ModShortcutDescriptionFormatter.IsPossibleLegacyDescription(binding.Action));
        if (hasExistingBindings && !mayContainLegacyAutomaticDescriptions)
        {
            return 0;
        }

        IReadOnlyList<DetectedModShortcut> detected;
        try
        {
            detected = await Task.Run(() => ModShortcutScanner.ScanDirectory(modFolder, ScanSafetyLimit));
        }
        catch (Exception ex)
        {
            _log("Read Mod shortcut INI", ex);
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
                _saveConfig();
            }

            return upgradedCount;
        }

        Bindings[modFolder] = detected
            .Take(ScanSafetyLimit)
            .Select(CreateAutomatic)
            .ToList();
        _saveConfig();
        return Bindings[modFolder].Count;
    }

    private string DescribeDetectedShortcut(DetectedModShortcut shortcut)
    {
        return ModShortcutDescriptionFormatter.Describe(
            shortcut,
            useEnglish: !_services.Language.IsChinese);
    }

    /// <summary>A binding read from a Mod's files, described in the current language.</summary>
    public ShortcutBinding CreateAutomatic(DetectedModShortcut shortcut)
    {
        return new ShortcutBinding(shortcut.Shortcut, DescribeDetectedShortcut(shortcut))
        {
            AutoSection = shortcut.Section,
            AutoBehavior = shortcut.Behavior,
            AutoTarget = shortcut.Target,
            AutoSourceFile = shortcut.SourceFile
        };
    }

    /// <summary>What a binding does: its automatic description in the current language, or the text the user wrote.</summary>
    public string DisplayAction(ShortcutBinding binding)
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

    private int UpgradeLegacyAutomaticShortcutBindings(
        List<ShortcutBinding> existingBindings,
        IReadOnlyList<DetectedModShortcut> detectedShortcuts)
    {
        int upgradedCount = 0;
        for (int index = 0; index < existingBindings.Count; index++)
        {
            ShortcutBinding existingBinding = existingBindings[index];
            if (existingBinding.HasAutoDescription
                || !ModShortcutDescriptionFormatter.IsPossibleLegacyDescription(existingBinding.Action))
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

            existingBindings[index] = CreateAutomatic(match);
            upgradedCount++;
        }

        return upgradedCount;
    }
}
