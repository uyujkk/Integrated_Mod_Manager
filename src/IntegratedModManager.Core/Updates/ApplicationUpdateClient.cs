using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IntegratedModManager.Core.Updates;

/// <summary>The latest GitHub release: its text, page, and the update package with its checksum file when it has them.</summary>
public sealed record LatestReleaseInfo(
    string Tag,
    string Title,
    string Body,
    string PublishedAt,
    string PageUrl,
    string? PackageUrl,
    string? Sha256Url);

/// <summary>
/// Reads the latest release and fetches its update package. The window shows progress and messages and
/// starts the updater; nothing here touches the installation.
/// </summary>
public static class ApplicationUpdateClient
{
    /// <summary>The release asset the updater installs, such as Integrated_Mod_Manager-v4.7.0.zip.</summary>
    public static readonly Regex PackageNamePattern = new(
        @"^Integrated_Mod_Manager-v(?<version>\d+\.\d+\.\d+)\.zip$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static async Task<LatestReleaseInfo> FetchLatestReleaseAsync(HttpClient client, string apiUrl, string fallbackPageUrl,
        CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, apiUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return ParseRelease(document.RootElement, fallbackPageUrl);
    }

    /// <summary>A GitHub release object; a missing page link falls back to the repository page.</summary>
    public static LatestReleaseInfo ParseRelease(JsonElement root, string fallbackPageUrl)
    {
        string tag = Text(root, "tag_name") ?? string.Empty;
        string? packageUrl = null;
        string? packageFileName = null;
        var checksumAssets = new List<(string Name, string DownloadUrl)>();

        if (root.TryGetProperty("assets", out JsonElement assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement asset in assets.EnumerateArray())
            {
                string name = Text(asset, "name") ?? string.Empty;
                string downloadUrl = Text(asset, "browser_download_url") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(downloadUrl))
                {
                    continue;
                }

                if (PackageNamePattern.IsMatch(name))
                {
                    packageUrl = downloadUrl;
                    packageFileName = name;
                }
                else if (name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
                {
                    checksumAssets.Add((name, downloadUrl));
                }
            }
        }

        string? sha256Url = packageFileName is null ? null : UpdateReleaseAssetSelector.SelectChecksumUrl(packageFileName, checksumAssets);
        return new LatestReleaseInfo(
            tag,
            Text(root, "name") ?? tag,
            (Text(root, "body") ?? string.Empty).Trim(),
            Text(root, "published_at") ?? string.Empty,
            Text(root, "html_url") ?? fallbackPageUrl,
            packageUrl,
            sha256Url);
    }

    /// <summary>Writes the package to <paramref name="path"/>, reporting bytes written and the size when the server sends one.</summary>
    public static async Task DownloadAsync(HttpClient client, string packageUrl, string path, Action<long, long?>? progress,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await client.GetAsync(packageUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        long? totalBytes = response.Content.Headers.ContentLength is > 0 ? response.Content.Headers.ContentLength : null;
        progress?.Invoke(0, totalBytes);
        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await UpdatePackageFile.WriteAsync(source, path, downloaded => progress?.Invoke(downloaded, totalBytes), cancellationToken);
    }

    /// <summary>Whether the downloaded file has the SHA-256 the release's checksum file lists for the package.</summary>
    public static async Task<bool> VerifyAsync(HttpClient client, string packageUrl, string sha256Url, string path,
        CancellationToken cancellationToken = default)
    {
        string checksumText = await client.GetStringAsync(sha256Url, cancellationToken);
        string expectedHash = UpdateChecksumParser.ParseSha256(checksumText,
            Path.GetFileName(new Uri(packageUrl).LocalPath), Path.GetFileName(new Uri(sha256Url).LocalPath));
        string actualHash = await UpdatePackageFile.ComputeSha256Async(path, cancellationToken);
        return actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase);
    }

    private static string? Text(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
