using IntegratedModManager.Core;

namespace IntegratedModManager.Core.Tests;

public sealed class CharacterFolderPlannerTests : IDisposable
{
    private const string Root = @"D:\Mods";
    private static readonly string[] Perlica = ["Perlica", "佩丽卡"];
    private readonly string temp = Path.Combine(Path.GetTempPath(), "imm-character-folders", Guid.NewGuid().ToString("N"));

    private static string[] Folders(params string[] names) => names.Select(name => Path.Combine(Root, name)).ToArray();

    private static CharacterFolderPlan Plan(string[] folders, string[]? files = null, string[]? aliases = null,
        string? character = "Perlica", string? zh = "佩丽卡", string? en = "Perlica", bool uiChinese = true)
        => CharacterFolderPlanner.Plan(Root, folders, files ?? [], aliases ?? Perlica, character, zh, en, uiChinese);

    [Theory]
    [InlineData("佩丽卡")]
    [InlineData("Perlica")]
    [InlineData("perlica mods")]
    public void ExistingChineseEnglishOrAliasFolderIsReused(string existing)
    {
        var plan = Plan(Folders("莱万汀", existing));
        Assert.Equal(CharacterFolderAction.UseExisting, plan.Action);
        Assert.Equal(Path.Combine(Root, existing), plan.Path);
    }

    [Fact]
    public void MissingFolderIsCreatedInTheRepositoryNamingLanguage()
    {
        Assert.Equal(Path.Combine(Root, "佩丽卡"), Plan(Folders("莱万汀", "祀")).Path);
        Assert.Equal(Path.Combine(Root, "Perlica"), Plan(Folders("Laevatain", "Si"), uiChinese: true).Path);
        Assert.Equal(CharacterFolderAction.Create, Plan(Folders("莱万汀")).Action);
    }

    [Fact]
    public void EmptyRepositoryFollowsTheInterfaceLanguage()
    {
        Assert.Equal(Path.Combine(Root, "佩丽卡"), Plan([], uiChinese: true).Path);
        Assert.Equal(Path.Combine(Root, "Perlica"), Plan([], uiChinese: false).Path);
        // Hidden and disabled folders do not decide the language.
        Assert.Equal(Path.Combine(Root, "Perlica"), Plan(Folders(".imm-cache", "DISABLED-旧"), uiChinese: false).Path);
    }

    [Theory]
    [InlineData(new[] { "莱万汀", "Laevatain" }, CharacterFolderReason.MixedNamingLanguage)]
    [InlineData(new[] { "Perlica A", "Perlica B" }, CharacterFolderReason.Ambiguous)]
    public void UncertainRepositoriesAskTheUser(string[] names, CharacterFolderReason reason)
    {
        var plan = Plan(Folders(names));
        Assert.Equal(CharacterFolderAction.AskUser, plan.Action);
        Assert.Equal(reason, plan.Reason);
        Assert.Null(plan.Path);
    }

    [Theory]
    [InlineData(null, CharacterFolderReason.UnknownCharacter)]
    [InlineData("Perlica & Akekuri", CharacterFolderReason.MultipleCharacters)]
    [InlineData("佩丽卡、余烬", CharacterFolderReason.MultipleCharacters)]
    public void UnknownOrMultipleCharactersAskTheUser(string? character, CharacterFolderReason reason)
        => Assert.Equal(reason, Plan(Folders("莱万汀"), character: character).Reason);

    [Fact]
    public void NoAliasesMeansUnknownCharacter()
        => Assert.Equal(CharacterFolderReason.UnknownCharacter, Plan(Folders("莱万汀"), aliases: []).Reason);

    [Fact]
    public void MissingNameInTheRepositoryLanguageAsksTheUser()
        => Assert.Equal(CharacterFolderReason.NoNameInLanguage, Plan(Folders("莱万汀"), zh: null).Reason);

    [Theory]
    [InlineData("CON")]
    [InlineData("nul.txt")]
    [InlineData("a/b")]
    [InlineData("name.")]
    [InlineData("a*b")]
    [InlineData("LPT1")]
    public void InvalidNamesAreNeverProposed(string name)
        => Assert.Equal(CharacterFolderReason.InvalidName, Plan([], en: name, uiChinese: false).Reason);

    [Fact]
    public void ASameNamedFileBlocksCreation()
        => Assert.Equal(CharacterFolderReason.NameConflict, Plan(Folders("莱万汀"), files: ["佩丽卡"]).Reason);

    [Fact]
    public void CreatePlannedFolderCreatesOnceAndReusesExisting()
    {
        Directory.CreateDirectory(temp);
        string planned = Path.Combine(temp, "佩丽卡");
        Assert.Equal(planned, CharacterFolderPlanner.CreatePlannedFolder(temp, planned));
        File.WriteAllText(Path.Combine(planned, "keep.txt"), "user file");
        // A concurrent download for the same character simply reuses it.
        Assert.Equal(planned, CharacterFolderPlanner.CreatePlannedFolder(temp, planned));
        Assert.True(File.Exists(Path.Combine(planned, "keep.txt")));
    }

    [Fact]
    public void CreatePlannedFolderRefusesOutsideTheRepositoryFilesAndLinks()
    {
        Directory.CreateDirectory(temp);
        Assert.Throws<IOException>(() => CharacterFolderPlanner.CreatePlannedFolder(temp, Path.Combine(temp, "a", "b")));
        Assert.Throws<IOException>(() => CharacterFolderPlanner.CreatePlannedFolder(temp, Path.Combine(temp, "..", "outside")));
        File.WriteAllText(Path.Combine(temp, "taken"), "file");
        Assert.Throws<IOException>(() => CharacterFolderPlanner.CreatePlannedFolder(temp, Path.Combine(temp, "taken")));
        string target = Path.Combine(temp, "elsewhere");
        Directory.CreateDirectory(target);
        DirectoryLinkDeployment.CreateJunction(Path.Combine(temp, "linked"), target);
        try { Assert.Throws<IOException>(() => CharacterFolderPlanner.CreatePlannedFolder(temp, Path.Combine(temp, "linked"))); }
        finally { DirectoryLinkDeployment.RemoveLink(Path.Combine(temp, "linked")); }
        Assert.Throws<DirectoryNotFoundException>(() => CharacterFolderPlanner.CreatePlannedFolder(Path.Combine(temp, "missing"), Path.Combine(temp, "missing", "x")));
    }

    public void Dispose() { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
}
