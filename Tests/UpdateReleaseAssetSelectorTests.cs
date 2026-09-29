using IntegratedModManager.Core;

namespace IntegratedModManager.Core.Tests;

public sealed class UpdateReleaseAssetSelectorTests
{
    private const string MainPackage = "Integrated_Mod_Manager-v3.9.5.zip";

    [Fact]
    public void SelectChecksumUrl_PrefersMatchingSidecarRegardlessOfAssetOrder()
    {
        (string Name, string DownloadUrl)[] assets =
        [
            (MainPackage + ".sha256", "https://example.test/main.sha256"),
            ("Integrated_Mod_Manager_Developer_Tools-v3.9.5.zip.sha256", "https://example.test/developer.sha256"),
            ("SHA256SUMS.txt", "https://example.test/all-checksums")
        ];

        Assert.Equal("https://example.test/main.sha256",
            UpdateReleaseAssetSelector.SelectChecksumUrl(MainPackage, assets));
    }

    [Fact]
    public void SelectChecksumUrl_UsesGenericChecksumWhenMatchingSidecarIsAbsent()
    {
        (string Name, string DownloadUrl)[] assets =
        [
            ("Integrated_Mod_Manager_Developer_Tools-v3.9.5.zip.sha256", "https://example.test/developer.sha256"),
            ("sha256sums.TXT", "https://example.test/all-checksums")
        ];

        Assert.Equal("https://example.test/all-checksums",
            UpdateReleaseAssetSelector.SelectChecksumUrl(MainPackage, assets));
    }

    [Fact]
    public void SelectChecksumUrl_DoesNotPairAnotherPackagesSidecar()
    {
        (string Name, string DownloadUrl)[] assets =
        [
            ("Integrated_Mod_Manager_Developer_Tools-v3.9.5.zip.sha256", "https://example.test/developer.sha256")
        ];

        Assert.Null(UpdateReleaseAssetSelector.SelectChecksumUrl(MainPackage, assets));
    }

    [Fact]
    public void SelectChecksumUrl_IgnoresAssetsWithoutDownloadUrls()
    {
        (string Name, string DownloadUrl)[] assets =
        [
            (MainPackage + ".sha256", string.Empty),
            ("SHA256SUMS.txt", "https://example.test/all-checksums")
        ];

        Assert.Equal("https://example.test/all-checksums",
            UpdateReleaseAssetSelector.SelectChecksumUrl(MainPackage, assets));
    }
}
