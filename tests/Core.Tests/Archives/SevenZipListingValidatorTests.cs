using IntegratedModManager.Core.Archives;

namespace IntegratedModManager.Core.Tests;

public sealed class SevenZipListingValidatorTests
{
    [Fact]
    public void Validate_AllowsEmptyLinkFieldsFromModernRarListing()
    {
        string listing = """
            Path = example.rar
            Type = Rar5
            ----------
            Path = Fangyi\UI\Panel.dds
            Folder = -
            Symbolic Link =
            Hard Link = -
            Copy Link =
            Unknown Property = ignored
            """;

        SevenZipListingValidator.Validate(listing, CreateDestinationPath());
    }

    [Theory]
    [InlineData("Symbolic Link = ..\\outside.ini")]
    [InlineData("Hard Link = Fangyi\\shared.ini")]
    [InlineData("Copy Link = Fangyi\\shared.ini")]
    [InlineData("Mode = lrwxrwxrwx")]
    public void Validate_RejectsActualLinks(string property)
    {
        string listing = $"----------\nPath = Fangyi\\mod.ini\n{property}";

        var error = Assert.Throws<InvalidDataException>(() =>
            SevenZipListingValidator.Validate(listing, CreateDestinationPath()));
        Assert.True(ArchiveErrors.Is(error, ArchiveErrorKind.LinkEntry));
    }

    [Fact]
    public void Validate_RejectsPasswordProtectedEntries()
    {
        const string listing = "----------\nPath = mod.ini\nEncrypted = +\nMethod = LZMA2:12 7zAES:19";

        var error = Assert.Throws<InvalidDataException>(() =>
            SevenZipListingValidator.Validate(listing, CreateDestinationPath()));
        Assert.True(ArchiveErrors.Is(error, ArchiveErrorKind.Encrypted));
    }

    [Fact]
    public void Validate_AllowsUnencryptedEntries()
    {
        const string listing = "----------\nPath = mod.ini\nEncrypted = -";

        SevenZipListingValidator.Validate(listing, CreateDestinationPath());
    }

    [Fact]
    public void Validate_RejectsEntryOutsideDestination()
    {
        const string listing = "----------\nPath = ..\\outside.ini";

        var error = Assert.Throws<InvalidDataException>(() =>
            SevenZipListingValidator.Validate(listing, CreateDestinationPath()));
        Assert.False(ArchiveErrors.Is(error, ArchiveErrorKind.LinkEntry));
    }

    [Fact]
    public void Validate_RejectsEmptyEntryPath()
    {
        const string listing = "----------\nPath =";

        Assert.Throws<InvalidDataException>(() =>
            SevenZipListingValidator.Validate(listing, CreateDestinationPath()));
    }

    private static string CreateDestinationPath()
    {
        return Path.Combine(Path.GetTempPath(), "imm-sevenzip-tests", Guid.NewGuid().ToString("N"));
    }
}
