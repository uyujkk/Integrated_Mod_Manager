namespace IntegratedModManager.Core.Online;

/// <summary>
/// Which online source a repository uses (stage J finish, step 3: moved unchanged from the Online page): the source
/// site, game name and GameBanana category it names, or the defaults, and whether that is Arknights: Endfield.
/// </summary>
public static class OnlineSourceRules
{
    public const string DefaultSourceSite = "GameBanana";

    /// <summary>Arknights: Endfield's GameBanana category, the built-in repository's default.</summary>
    public const string DefaultCategoryId = "42770";

    public const string EndfieldOfficialWikiUrl = "https://wiki.skland.com/endfield";

    /// <summary>The repository's source site, or GameBanana when it names none.</summary>
    public static string Source(string? sourceSite)
        => string.IsNullOrWhiteSpace(sourceSite) ? DefaultSourceSite : sourceSite.Trim();

    /// <summary>A repository's game name or category id as set, trimmed; empty when not set.</summary>
    public static string Trimmed(string? value) => value?.Trim() ?? string.Empty;

    /// <summary>The Online page reads GameBanana; an empty source counts as GameBanana.</summary>
    public static bool IsGameBanana(string? source)
        => string.IsNullOrWhiteSpace(source) || source.Contains("gamebanana", StringComparison.OrdinalIgnoreCase);

    /// <summary>A repository for Arknights: Endfield, by its category or its game name.</summary>
    public static bool IsEndfield(string categoryId, string gameName)
        => string.Equals(categoryId, DefaultCategoryId, StringComparison.OrdinalIgnoreCase)
            || gameName.Contains("Endfield", StringComparison.OrdinalIgnoreCase)
            || gameName.Contains("终末地", StringComparison.OrdinalIgnoreCase);

    /// <summary>The Endfield category, or one of its operators' own categories.</summary>
    public static bool IsEndfieldCategory(string? categoryId, IEnumerable<string?> operatorCategoryIds)
        => !string.IsNullOrWhiteSpace(categoryId)
            && (string.Equals(categoryId, DefaultCategoryId, StringComparison.OrdinalIgnoreCase)
                || operatorCategoryIds.Any(id => string.Equals(id, categoryId, StringComparison.OrdinalIgnoreCase)));
}
