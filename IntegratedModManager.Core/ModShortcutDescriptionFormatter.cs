using System.Text.RegularExpressions;

namespace IntegratedModManager.Core;

public static class ModShortcutDescriptionFormatter
{
    private static readonly Regex CamelCaseBoundary = new(
        "(?<=[a-z0-9])(?=[A-Z])",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex NonWordSeparators = new(
        @"[^\p{L}\p{N}]+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex Whitespace = new(
        @"\s+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly HashSet<string> TechnicalTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd", "command", "commandlist", "config", "cfg", "key", "mod", "option",
        "state", "swap", "swapvar", "toggle", "var", "variable"
    };

    private static readonly IReadOnlyDictionary<string, (string Zh, string En)> ExactTargets =
        new Dictionary<string, (string Zh, string En)>(StringComparer.OrdinalIgnoreCase)
        {
            ["help"] = ("帮助界面", "Help overlay"),
            ["guide"] = ("操作指引", "User guide"),
            ["imgx"] = ("图像横向选项", "Horizontal image option"),
            ["imgy"] = ("图像纵向选项", "Vertical image option"),
            ["imagex"] = ("图像横向选项", "Horizontal image option"),
            ["imagey"] = ("图像纵向选项", "Vertical image option"),
            ["mouseclicked"] = ("鼠标点击状态", "Mouse-click state"),
            ["hair"] = ("发型", "Hairstyle"),
            ["hairacc"] = ("发饰", "Hair accessory"),
            ["hairaccessory"] = ("发饰", "Hair accessory"),
            ["horn"] = ("角饰", "Horns"),
            ["horns"] = ("角饰", "Horns"),
            ["choker"] = ("颈饰", "Choker"),
            ["bodysuit"] = ("连体服", "Bodysuit"),
            ["panty"] = ("内裤", "Underwear"),
            ["panties"] = ("内裤", "Underwear"),
            ["underwear"] = ("内衣", "Underwear"),
            ["bracelet"] = ("手镯", "Bracelet"),
            ["necklace"] = ("项链", "Necklace"),
            ["earring"] = ("耳饰", "Earrings"),
            ["earrings"] = ("耳饰", "Earrings"),
            ["glasses"] = ("眼镜", "Glasses"),
            ["mask"] = ("面具", "Mask"),
            ["hat"] = ("帽子", "Hat"),
            ["coat"] = ("外套", "Coat"),
            ["jacket"] = ("夹克", "Jacket"),
            ["shirt"] = ("上衣", "Shirt"),
            ["top"] = ("上装", "Top"),
            ["bottom"] = ("下装", "Bottom"),
            ["pants"] = ("裤装", "Pants"),
            ["trousers"] = ("裤装", "Trousers"),
            ["skirt"] = ("裙装", "Skirt"),
            ["dress"] = ("连衣裙", "Dress"),
            ["outfit"] = ("服装", "Outfit"),
            ["costume"] = ("服装", "Costume"),
            ["shoes"] = ("鞋子", "Shoes"),
            ["boots"] = ("靴子", "Boots"),
            ["socks"] = ("袜子", "Socks"),
            ["stockings"] = ("丝袜", "Stockings"),
            ["glove"] = ("手套", "Gloves"),
            ["gloves"] = ("手套", "Gloves"),
            ["sleeve"] = ("袖子", "Sleeves"),
            ["sleeves"] = ("袖子", "Sleeves"),
            ["cape"] = ("披风", "Cape"),
            ["bra"] = ("胸衣", "Bra"),
            ["body"] = ("身体部件", "Body option"),
            ["face"] = ("面部", "Face"),
            ["eyes"] = ("眼睛", "Eyes"),
            ["eye"] = ("眼睛", "Eyes"),
            ["makeup"] = ("妆容", "Makeup"),
            ["tattoo"] = ("纹身", "Tattoo"),
            ["nails"] = ("美甲", "Nails"),
            ["tail"] = ("尾巴", "Tail"),
            ["ears"] = ("耳朵", "Ears"),
            ["wings"] = ("翅膀", "Wings"),
            ["wing"] = ("翅膀", "Wings"),
            ["weapon"] = ("武器显示", "Weapon visibility"),
            ["accessory"] = ("配饰", "Accessory"),
            ["accessories"] = ("配饰", "Accessories"),
            ["acc"] = ("配饰", "Accessory"),
            ["color"] = ("配色", "Color scheme"),
            ["colour"] = ("配色", "Color scheme"),
            ["texture"] = ("材质", "Texture"),
            ["menu"] = ("Mod 菜单", "Mod menu"),
            ["reset"] = ("重置设置", "Reset settings")
        };

    private static readonly IReadOnlyDictionary<string, (string Zh, string En)> TokenTargets =
        new Dictionary<string, (string Zh, string En)>(StringComparer.OrdinalIgnoreCase)
        {
            ["open"] = ("打开", "Open"),
            ["close"] = ("关闭", "Close"),
            ["reset"] = ("重置", "Reset"),
            ["position"] = ("位置", "position"),
            ["menu"] = ("菜单", "menu"),
            ["character"] = ("角色", "character"),
            ["image"] = ("图像", "image"),
            ["img"] = ("图像", "image"),
            ["hair"] = ("头发", "hair"),
            ["accessory"] = ("配饰", "accessory"),
            ["acc"] = ("配饰", "accessory"),
            ["visibility"] = ("显示", "visibility"),
            ["show"] = ("显示", "show"),
            ["hide"] = ("隐藏", "hide"),
            ["color"] = ("颜色", "color"),
            ["colour"] = ("颜色", "color"),
            ["x"] = ("横向", "X"),
            ["y"] = ("纵向", "Y")
        };

    public static string Describe(DetectedModShortcut shortcut, bool useEnglish)
    {
        string target = FormatTarget(shortcut.Target, shortcut.Section, useEnglish);
        return shortcut.Behavior.Trim().ToLowerInvariant() switch
        {
            "cycle" => useEnglish ? $"Cycle: {target}" : $"循环选择：{target}",
            "hold" => useEnglish ? $"Hold to activate: {target}" : $"按住生效：{target}",
            "toggle" => useEnglish ? $"Toggle on/off: {target}" : $"开启/关闭：{target}",
            "run" => useEnglish ? $"Run: {target}" : $"执行：{target}",
            _ => useEnglish ? $"Activate: {target}" : $"触发：{target}"
        };
    }

    public static bool MatchesLegacyDescription(string? description, DetectedModShortcut shortcut)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return false;
        }

        string target = string.IsNullOrWhiteSpace(shortcut.Target) ? shortcut.Section : shortcut.Target;
        (string Zh, string En) legacy = shortcut.Behavior.Trim().ToLowerInvariant() switch
        {
            "cycle" => ($"循环切换 {target}", $"Cycle {target}"),
            "hold" => ($"按住触发 {target}", $"Hold {target}"),
            "toggle" => ($"切换 {target}", $"Toggle {target}"),
            _ => ($"触发 {target}", $"Trigger {target}")
        };
        return string.Equals(description.Trim(), legacy.Zh, StringComparison.OrdinalIgnoreCase)
            || string.Equals(description.Trim(), legacy.En, StringComparison.OrdinalIgnoreCase);
    }

    public static string FormatTarget(string? target, string? section, bool useEnglish)
    {
        string source = string.IsNullOrWhiteSpace(target) ? section ?? string.Empty : target;
        string humanized = HumanizeIdentifier(source);
        string compactKey = NonWordSeparators.Replace(humanized, string.Empty).ToLowerInvariant();
        if (ExactTargets.TryGetValue(compactKey, out (string Zh, string En) exact))
        {
            return useEnglish ? exact.En : exact.Zh;
        }

        string[] tokens = humanized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => !TechnicalTokens.Contains(token))
            .ToArray();
        if (tokens.Length == 0)
        {
            return useEnglish ? "Mod option" : "Mod 选项";
        }

        bool translatedAny = false;
        var translated = new List<string>(tokens.Length);
        foreach (string token in tokens)
        {
            if (TokenTargets.TryGetValue(token, out (string Zh, string En) localized))
            {
                translated.Add(useEnglish ? localized.En : localized.Zh);
                translatedAny = true;
            }
            else if (!int.TryParse(token, out _))
            {
                translated.Add(useEnglish ? ToEnglishDisplayToken(token) : token);
            }
        }

        if (translated.Count == 0)
        {
            return useEnglish ? "Mod option" : "Mod 选项";
        }

        string label = string.Join(useEnglish ? " " : string.Empty, translated);
        if (!useEnglish && !translatedAny)
        {
            return $"“{label}”选项";
        }

        return useEnglish ? UppercaseFirst(label) : label;
    }

    private static string HumanizeIdentifier(string value)
    {
        string withoutVariableMarker = value.Trim().TrimStart('$');
        string separated = CamelCaseBoundary.Replace(withoutVariableMarker, " ");
        separated = NonWordSeparators.Replace(separated, " ");
        return Whitespace.Replace(separated, " ").Trim();
    }

    private static string ToEnglishDisplayToken(string token) =>
        token.Length <= 2 && token.All(char.IsUpper)
            ? token
            : token.ToLowerInvariant();

    private static string UppercaseFirst(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
