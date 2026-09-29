namespace IntegratedModManager.Core;

public static class UpdateReleaseAssetSelector
{
    public static string? SelectChecksumUrl(
        string packageFileName,
        IEnumerable<(string Name, string DownloadUrl)> checksumAssets)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageFileName);
        ArgumentNullException.ThrowIfNull(checksumAssets);

        var available = checksumAssets
            .Where(asset => !string.IsNullOrWhiteSpace(asset.DownloadUrl))
            .ToList();

        string matchingSidecarName = packageFileName + ".sha256";
        foreach ((string name, string downloadUrl) in available)
        {
            if (string.Equals(name, matchingSidecarName, StringComparison.OrdinalIgnoreCase))
            {
                return downloadUrl;
            }
        }

        foreach ((string name, string downloadUrl) in available)
        {
            if (string.Equals(name, "SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
            {
                return downloadUrl;
            }
        }

        return null;
    }
}
