using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Shortcuts;

namespace IntegratedModManager.Core.Tests;

public sealed class ModIniConflictDetectorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "imm-ini-conflicts-" + Guid.NewGuid().ToString("N"));

    public ModIniConflictDetectorTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private const string SkinA = """
        [Constants]
        global $active = 0

        [KeySwap]
        condition = $active == 1
        key = VK_F6
        $swapvar = 0,1

        [TextureOverrideBodyPosition]
        hash = 1A2B3C4D ; vertex buffer
        vb0 = ResourceBodyPosition
        $active = 1

        [TextureOverrideBodyIB]
        hash = 0x99aa88bb
        handling = skip

        [TextureOverrideBodyHead]
        hash = 99aa88bb
        match_first_index = 0
        ib = ResourceHeadIB

        [ShaderOverrideOutline]
        hash = 0123456789abcdef
        run = CommandListOutline
        """;

    [Fact]
    public void ParseTextReadsOverridesShortcutsAndConditions()
    {
        ModIniProfile profile = ModIniConflictDetector.ParseText(SkinA, "skin.ini");

        Assert.Equal(4, profile.Overrides.Count);
        ModIniHashOverride position = Assert.Single(profile.Overrides, item => item.Section == "TextureOverrideBodyPosition");
        Assert.Equal("1a2b3c4d", position.Hash);
        Assert.Equal("TextureOverride", position.SectionType);
        Assert.Null(position.MatchFirstIndex);
        Assert.Equal("skin.ini", position.SourceFile);
        Assert.Equal("0", Assert.Single(profile.Overrides, item => item.Section == "TextureOverrideBodyHead").MatchFirstIndex);
        Assert.Equal("99aa88bb", Assert.Single(profile.Overrides, item => item.Section == "TextureOverrideBodyIB").Hash);
        Assert.Equal("ShaderOverride", Assert.Single(profile.Overrides, item => item.Section == "ShaderOverrideOutline").SectionType);

        DetectedModShortcut key = Assert.Single(profile.Shortcuts);
        Assert.Equal("F6", key.Shortcut);
        Assert.True(profile.IsConditional(key));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankTextIsEmpty(string text)
    {
        ModIniProfile profile = ModIniConflictDetector.ParseText(text);

        Assert.Empty(profile.Overrides);
        Assert.Empty(profile.Shortcuts);
    }

    [Fact]
    public void InvalidHashesAndExplicitPrioritiesAreIgnored()
    {
        const string text = """
            [TextureOverrideNoHash]
            vb0 = ResourceA

            [TextureOverrideBadHash]
            hash = not-a-hash

            [TextureOverrideResolved]
            hash = aabbccdd
            match_priority = 5

            [ShaderOverrideShared]
            hash = 0011223344556677
            allow_duplicate_hash = overrule

            [ResourceNotAnOverride]
            hash = aabbccdd
            """;

        Assert.Empty(ModIniConflictDetector.ParseText(text).Overrides);
        Assert.Single(ModIniConflictDetector.ParseText("[ShaderOverrideA]\nhash = 0011223344556677\nallow_duplicate_hash = false\n").Overrides);
    }

    [Fact]
    public void SameHashOnSameCharacterIsReportedWithSharedKeys()
    {
        string candidate = CreateMod("Candidate", ("mod.ini", SkinA));
        string target = Path.Combine(_root, "Mods");
        CreateMod(Path.Combine("Mods", "OtherSkin"), ("mod.ini", SkinA.Replace("ShaderOverrideOutline", "ShaderOverrideOther")));

        IReadOnlyList<ModIniConflictSummary> conflicts = ModIniConflictDetector.FindConflictsInTarget(candidate, target, []);

        ModIniConflictSummary hashes = Assert.Single(conflicts, item => item.Kind == ModIniConflictKind.ResourceHash);
        Assert.Equal("OtherSkin", hashes.InstalledModName);
        // Position VB, IB (no index) and IB@0 overlap; the outline shader too.
        Assert.Equal(4, hashes.Count);
        ModIniConflictSummary keys = Assert.Single(conflicts, item => item.Kind == ModIniConflictKind.Shortcut);
        Assert.Equal("F6", keys.Example);
        Assert.Equal("KeySwap", keys.CandidateSection);
        Assert.Equal(ModIniConflictKind.ResourceHash, conflicts[0].Kind);
    }

    [Fact]
    public void DifferentDrawIndicesDoNotOverlapButUnindexedOverridesDo()
    {
        ModIniProfile candidate = ModIniConflictDetector.ParseText("""
            [TextureOverrideHead]
            hash = 11112222
            match_first_index = 0
            """);
        ModIniProfile otherIndex = ModIniConflictDetector.ParseText("""
            [TextureOverrideBody]
            hash = 11112222
            match_first_index = 4500
            """);
        ModIniProfile unindexed = ModIniConflictDetector.ParseText("""
            [TextureOverrideAll]
            hash = 11112222
            """);

        IReadOnlyList<ModIniConflictSummary> conflicts = ModIniConflictDetector.FindConflicts(
            candidate, [("Other index", otherIndex), ("Unindexed", unindexed)]);

        ModIniConflictSummary summary = Assert.Single(conflicts);
        Assert.Equal("Unindexed", summary.InstalledModName);
        Assert.Equal("11112222@0", summary.Example);
        Assert.Equal("TextureOverrideHead", summary.CandidateSection);
        Assert.Equal("TextureOverrideAll", summary.InstalledSection);
    }

    [Fact]
    public void TextureAndShaderHashesAreSeparateNamespaces()
    {
        ModIniProfile candidate = ModIniConflictDetector.ParseText("[TextureOverrideA]\nhash = abcdef12\n");
        ModIniProfile installed = ModIniConflictDetector.ParseText("[ShaderOverrideA]\nhash = abcdef12\n");

        Assert.Empty(ModIniConflictDetector.FindConflicts(candidate, [("Shader", installed)]));
    }

    [Fact]
    public void ConditionalKeysOnDifferentCharactersAreNotConflicts()
    {
        ModIniProfile candidate = ModIniConflictDetector.ParseText("""
            [KeyOutfit]
            condition = $active == 1
            key = VK_F6
            [TextureOverrideCharacterA]
            hash = aaaa0001
            """);
        ModIniProfile otherCharacter = ModIniConflictDetector.ParseText("""
            [KeyOutfit]
            condition = $active == 1
            key = F6
            [TextureOverrideCharacterB]
            hash = bbbb0002
            """);
        ModIniProfile globalKey = ModIniConflictDetector.ParseText("""
            [KeyToggleUi]
            key = no_modifiers F6
            $ui = 1
            """);

        IReadOnlyList<ModIniConflictSummary> conflicts = ModIniConflictDetector.FindConflicts(
            candidate, [("Other character", otherCharacter), ("Global toggle", globalKey)]);

        ModIniConflictSummary summary = Assert.Single(conflicts);
        Assert.Equal("Global toggle", summary.InstalledModName);
        Assert.Equal(ModIniConflictKind.Shortcut, summary.Kind);
        Assert.Equal("F6", summary.Example);
        Assert.Equal("KeyToggleUi", summary.InstalledSection);
    }

    [Fact]
    public void DisabledFilesFoldersAndIgnoredTargetsAreSkipped()
    {
        string candidate = CreateMod("Candidate", ("mod.ini", SkinA));
        string target = Path.Combine(_root, "Mods");
        CreateMod(Path.Combine("Mods", "DISABLED_OldSkin"), ("mod.ini", SkinA));
        CreateMod(Path.Combine("Mods", "InactiveVariant"),
            ("DISABLED_mod.ini", SkinA),
            (Path.Combine("DISABLED variants", "alt.ini"), SkinA));
        string replaced = CreateMod(Path.Combine("Mods", "Replaced"), ("mod.ini", SkinA));
        string deployedCopy = CreateMod(Path.Combine("Mods", "Candidate"), ("mod.ini", SkinA));

        IReadOnlyList<ModIniConflictSummary> conflicts = ModIniConflictDetector.FindConflictsInTarget(
            candidate, target, [replaced + Path.DirectorySeparatorChar, deployedCopy]);

        Assert.Empty(conflicts);
        Assert.True(ModIniConflictDetector.IsDisabledName("disabled_skin"));
        Assert.False(ModIniConflictDetector.IsDisabledName("Skin DISABLED"));
    }

    [Fact]
    public void BrokenDeploymentLinksAreSkippedInsteadOfAbortingTheScan()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string candidate = CreateMod("Candidate", ("mod.ini", SkinA));
        string target = Path.Combine(_root, "Mods");
        CreateMod(Path.Combine("Mods", "OtherSkin"), ("mod.ini", SkinA.Replace("ShaderOverrideOutline", "ShaderOverrideOther")));
        string deleted = CreateMod("DeletedSource", ("mod.ini", SkinA));
        string stale = Path.Combine(target, "DeletedSource");
        DirectoryLinkDeployment.CreateJunction(stale, deleted);
        Directory.Delete(deleted, recursive: true);
        try
        {
            IReadOnlyList<ModIniConflictSummary> conflicts = ModIniConflictDetector.FindConflictsInTarget(candidate, target, []);

            Assert.All(conflicts, item => Assert.Equal("OtherSkin", item.InstalledModName));
            Assert.Contains(conflicts, item => item.Kind == ModIniConflictKind.ResourceHash);
        }
        finally
        {
            Directory.Delete(stale, recursive: false);
        }
    }

    [Fact]
    public void ScanDirectoryCombinesNestedFilesAndRespectsLimits()
    {
        string mod = CreateMod("Nested",
            ("a.ini", "[TextureOverrideA]\nhash = aaaaaaaa\n"),
            (Path.Combine("parts", "b.ini"), "[KeyB]\nkey = VK_F7\ncondition = $active\n"),
            ("huge.ini", "[TextureOverrideHuge]\nhash = cccccccc\n" + new string(';', 2 * 1024 * 1024 + 1)),
            ("notes.txt", "[TextureOverrideIgnored]\nhash = dddddddd\n"));

        ModIniProfile profile = ModIniConflictDetector.ScanDirectory(mod);
        ModIniProfile limited = ModIniConflictDetector.ScanDirectory(mod, maximumFiles: 1);

        Assert.Equal("aaaaaaaa", Assert.Single(profile.Overrides).Hash);
        DetectedModShortcut key = Assert.Single(profile.Shortcuts);
        Assert.Equal(Path.Combine("parts", "b.ini"), key.SourceFile);
        Assert.True(profile.IsConditional(key));
        Assert.Single(limited.Overrides);
        Assert.Empty(limited.Shortcuts);
    }

    [Fact]
    public void MissingOrEmptyInputsProduceNoConflicts()
    {
        string candidate = CreateMod("Candidate", ("mod.ini", SkinA));
        string emptyCandidate = CreateMod("Empty", ("readme.ini", "; nothing to compare\n"));
        string target = Path.Combine(_root, "Mods");
        CreateMod(Path.Combine("Mods", "OtherSkin"), ("mod.ini", SkinA));

        Assert.Empty(ModIniConflictDetector.FindConflictsInTarget(candidate, Path.Combine(_root, "missing"), []));
        Assert.Empty(ModIniConflictDetector.FindConflictsInTarget(emptyCandidate, target, []));
        Assert.Empty(ModIniConflictDetector.ScanDirectory(Path.Combine(_root, "missing")).Overrides);
        Assert.Empty(ModIniConflictDetector.ScanDirectory(" ").Overrides);
        Assert.Empty(ModIniConflictDetector.ScanDirectory(candidate, maximumFiles: 0).Overrides);
        Assert.Empty(ModIniConflictDetector.FindConflictsInTarget(candidate, target, [], maximumInstalledMods: 0));
    }

    [Fact]
    public void SummariesAreOrderedByKindThenCount()
    {
        ModIniProfile candidate = ModIniConflictDetector.ParseText("""
            [TextureOverrideA]
            hash = 00000001
            [TextureOverrideB]
            hash = 00000002
            [KeyGlobal]
            key = VK_F9
            """);
        ModIniProfile one = ModIniConflictDetector.ParseText("[TextureOverrideX]\nhash = 00000001\n");
        ModIniProfile two = ModIniConflictDetector.ParseText("[TextureOverrideX]\nhash = 00000001\n[TextureOverrideY]\nhash = 00000002\n");
        ModIniProfile key = ModIniConflictDetector.ParseText("[KeyOther]\nkey = F9\n");

        IReadOnlyList<ModIniConflictSummary> conflicts = ModIniConflictDetector.FindConflicts(
            candidate, [("Key only", key), ("One", one), ("Two", two)]);

        Assert.Equal(new[] { "Two", "One", "Key only" }, conflicts.Select(item => item.InstalledModName));
        Assert.Equal(2, conflicts[0].Count);
    }

    private string CreateMod(string relativeDirectory, params (string File, string Content)[] files)
    {
        string directory = Path.Combine(_root, relativeDirectory);
        Directory.CreateDirectory(directory);
        foreach ((string file, string content) in files)
        {
            string path = Path.Combine(directory, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        return directory;
    }
}
