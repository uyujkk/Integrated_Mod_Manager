using IntegratedModManager.Core.Endfield;

namespace IntegratedModManager.Core.Tests;

public sealed class EndfieldBridgeIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "bridge-tests", Guid.NewGuid().ToString("N"));
    private string Tools => Path.Combine(_root, "tools");
    public EndfieldBridgeIntegrationTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("42770", "", true)]
    [InlineData(null, "Arknights: Endfield", true)]
    [InlineData(null, "明日方舟：终末地", true)]
    [InlineData(null, "终末地", true)]
    [InlineData("30305", "Zenless Zone Zero", false)]
    [InlineData(null, null, false)]
    public void OfferOnlyForEndfield(string? category, string? name, bool expected) =>
        Assert.Equal(expected, EndfieldBridgeIntegration.IsEndfield(category, name));

    [Fact]
    public void LoaderChoicePreservesExplicitPathsAndUsesOnlyExistingDefaults()
    {
        string known = Path.Combine(_root, "XXMI Launcher.exe");
        File.WriteAllText(known, "test fixture - never executed");
        Assert.Equal("custom.exe", EndfieldBridgeIntegration.ResolveLoader(" custom.exe ", [known]));
        Assert.Equal(known, EndfieldBridgeIntegration.ResolveLoader(null, ["missing.exe", known]));
        Assert.Equal("", EndfieldBridgeIntegration.ResolveLoader("", ["missing.exe"]));
    }

    [Fact]
    public void LegacyInstallationNeedsExecutableAndSavedSettings()
    {
        string legacy = EndfieldBridgeIntegration.LegacyInstallDirectory(Tools);
        Assert.Equal(Path.Combine(Tools, "EndfieldBridge-0.3.2-win-x64"), legacy);
        Assert.False(EndfieldBridgeIntegration.HasLegacyInstallation(Tools));

        Directory.CreateDirectory(Path.Combine(legacy, "data"));
        File.WriteAllText(Path.Combine(legacy, "EndfieldBridge.exe"), "fixture - never executed");
        Assert.False(EndfieldBridgeIntegration.HasLegacyInstallation(Tools));

        File.WriteAllText(Path.Combine(legacy, "data", "profile.json"), "{}");
        Assert.True(EndfieldBridgeIntegration.HasLegacyInstallation(Tools));

        File.Delete(Path.Combine(legacy, "data", "profile.json"));
        File.WriteAllText(Path.Combine(legacy, "profile.json"), "{}");
        Assert.True(EndfieldBridgeIntegration.HasLegacyInstallation(Tools));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
