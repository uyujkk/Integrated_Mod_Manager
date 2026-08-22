using IntegratedModManager.Core;

namespace IntegratedModManager.Core.Tests;

public sealed class UpdateChecksumParserTests
{
    private const string PackageName = "Integrated_Mod_Manager-v3.8.0.zip";
    private static readonly string Hash = new('A', 64);

    [Fact]
    public void ParseSha256_AcceptsCommonFormat()
    {
        string result = UpdateChecksumParser.ParseSha256($"{Hash}  {PackageName}", PackageName, "SHA256SUMS.txt");

        Assert.Equal(Hash, result);
    }

    [Fact]
    public void ParseSha256_AcceptsBinaryMarkerFormat()
    {
        string result = UpdateChecksumParser.ParseSha256($"{Hash} *{PackageName}", PackageName, "SHA256SUMS.txt");

        Assert.Equal(Hash, result);
    }

    [Fact]
    public void ParseSha256_AcceptsBsdFormat()
    {
        string result = UpdateChecksumParser.ParseSha256($"SHA256 ({PackageName}) = {Hash}", PackageName, "checksums.txt");

        Assert.Equal(Hash, result);
    }

    [Fact]
    public void ParseSha256_AcceptsSingleHashForMatchingSidecarName()
    {
        string result = UpdateChecksumParser.ParseSha256(Hash, PackageName, PackageName + ".sha256");

        Assert.Equal(Hash, result);
    }

    [Fact]
    public void ParseSha256_RejectsEntryForDifferentPackage()
    {
        Assert.Throws<InvalidDataException>(() =>
            UpdateChecksumParser.ParseSha256($"{Hash}  another-package.zip", PackageName, "SHA256SUMS.txt"));
    }

    [Fact]
    public void ParseSha256_RejectsSingleHashWithGenericFileName()
    {
        Assert.Throws<InvalidDataException>(() =>
            UpdateChecksumParser.ParseSha256(Hash, PackageName, "checksum.txt"));
    }

    [Fact]
    public void ParseSha256_RejectsMalformedHash()
    {
        Assert.Throws<InvalidDataException>(() =>
            UpdateChecksumParser.ParseSha256("not-a-sha256", PackageName, PackageName + ".sha256"));
    }
}
