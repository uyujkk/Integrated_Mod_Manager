namespace IntegratedModManager.Core;

public static class OnlineInstallationDetectionPolicy
{
    public static bool Matches(
        int onlineItemId,
        string? onlineProfileUrl,
        int installedItemId,
        string? installedProfileUrl)
    {
        string onlineIdentity = BuildIdentity(onlineItemId, onlineProfileUrl);
        string installedIdentity = BuildIdentity(installedItemId, installedProfileUrl);
        return onlineIdentity.Length > 0
            && string.Equals(onlineIdentity, installedIdentity, StringComparison.OrdinalIgnoreCase);
    }

    public static string BuildIdentity(int itemId, string? profileUrl)
    {
        if (itemId > 0)
        {
            return "itemid:" + itemId;
        }

        if (string.IsNullOrWhiteSpace(profileUrl))
        {
            return string.Empty;
        }

        return "url:" + profileUrl.Trim().TrimEnd('/').ToLowerInvariant();
    }
}
