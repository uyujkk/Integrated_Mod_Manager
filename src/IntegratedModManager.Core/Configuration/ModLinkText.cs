namespace IntegratedModManager.Core.Configuration;

/// <summary>A Mod's link as the workspace saves it (moved from the window in stage J finish, step 8b).</summary>
public static class ModLinkText
{
    /// <summary>The link trimmed, with "https://" in front when it has no http or https scheme.</summary>
    public static string Normalize(string link)
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
