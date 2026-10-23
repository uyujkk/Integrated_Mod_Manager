using IntegratedModManager.Core;

namespace IntegratedModManager.Core.Tests;

public sealed class ArchiveImportLayoutTests : IDisposable
{
    private static readonly string[] Extensions = [".tar.gz", ".zip", ".7z", ".rar", ".tar", ".gz"];
    private readonly string _root = Path.Combine(Path.GetTempPath(), "imm-archive-layout-" + Guid.NewGuid().ToString("N"));

    public ArchiveImportLayoutTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void FlatArchiveGetsItsOwnModFolderAndNothingScattersIntoTheCharacterFolder()
    {
        string extracted = Extracted("flat",
            "mod.ini", "Body.ib", Path.Combine("textures", "Body Diffuse.dds"));
        string character = CharacterFolder("角色 A", "Existing Mod");

        ArchiveImportPlan plan = ArchiveImportLayout.Plan(extracted, @"C:\Downloads\水墨旗袍 v5.2.zip", Extensions);
        string installed = ArchiveImportLayout.Install(plan, character);

        Assert.Equal(ArchiveLayoutKind.Mixed, plan.Kind);
        Assert.Equal(Path.Combine(character, "水墨旗袍 v5.2"), installed);
        Assert.True(File.Exists(Path.Combine(installed, "mod.ini")));
        Assert.True(File.Exists(Path.Combine(installed, "textures", "Body Diffuse.dds")));
        Assert.Equal(new[] { "Existing Mod", "水墨旗袍 v5.2" }, Directory.GetDirectories(character).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Empty(Directory.GetFiles(character));
        Assert.False(new DirectoryInfo(installed).Attributes.HasFlag(FileAttributes.Hidden));
    }

    [Fact]
    public void ArchiveWithOneTopLevelFolderIsNotNestedAgain()
    {
        string extracted = Extracted("single",
            Path.Combine("Skin", "mod.ini"), Path.Combine("Skin", "res", "a.dds"), Path.Combine("__MACOSX", "Skin", "._mod.ini"), "Thumbs.db");
        string character = CharacterFolder("Char");

        ArchiveImportPlan plan = ArchiveImportLayout.Plan(extracted, "Other name.7z", Extensions);
        string installed = ArchiveImportLayout.Install(plan, character);

        Assert.Equal(ArchiveLayoutKind.SingleFolder, plan.Kind);
        Assert.Equal(Path.Combine(character, "Skin"), installed);
        Assert.True(File.Exists(Path.Combine(installed, "res", "a.dds")));
        Assert.False(Directory.Exists(Path.Combine(installed, "Skin")));
        Assert.False(Directory.Exists(Path.Combine(installed, "__MACOSX")));
    }

    [Theory]
    [InlineData(new[] { "a.ini", "b.buf" }, ArchiveLayoutKind.FlatFiles)]
    [InlineData(new[] { "a.ini", "Folder/b.buf" }, ArchiveLayoutKind.Mixed)]
    [InlineData(new[] { "One/a.ini", "Two/b.ini" }, ArchiveLayoutKind.MultipleFolders)]
    [InlineData(new[] { "One/a.ini", "desktop.ini", ".DS_Store" }, ArchiveLayoutKind.SingleFolder)]
    public void LayoutIsClassifiedIgnoringArchiveClutter(string[] files, ArchiveLayoutKind expected)
    {
        string extracted = Extracted("kind", files.Select(file => file.Replace('/', Path.DirectorySeparatorChar)).ToArray());

        ArchiveImportPlan plan = ArchiveImportLayout.Plan(extracted, "Pack.zip", Extensions);

        Assert.Equal(expected, plan.Kind);
        Assert.Equal(expected == ArchiveLayoutKind.SingleFolder ? "One" : "Pack", plan.ModFolderName);
    }

    [Fact]
    public void EmptyArchiveIsRejected()
    {
        string extracted = Extracted("empty", "__MACOSX/x".Replace('/', Path.DirectorySeparatorChar));

        var error = Assert.Throws<InvalidDataException>(() => ArchiveImportLayout.Plan(extracted, "Empty.zip", Extensions));
        Assert.True(ArchiveErrors.Is(error, ArchiveErrorKind.Empty));
        Assert.False(ArchiveErrors.Is(error, ArchiveErrorKind.LinkEntry));
    }

    [Theory]
    [InlineData("My Mod.tar.gz", "My Mod")]
    [InlineData("Skin.tar", "Skin")]
    [InlineData("name. .7z", "name")]
    [InlineData("CON.zip", "ImportedMod")]
    [InlineData("com1.rar", "ImportedMod")]
    [InlineData(".zip", "ImportedMod")]
    [InlineData(".imm-import-x.zip", "ImportedMod")]
    [InlineData("a|b?.zip", "a_b_")]
    [InlineData("Console.zip", "Console")]
    public void FolderNameFromArchiveIsAValidWindowsName(string archive, string expected) =>
        Assert.Equal(expected, ArchiveImportLayout.FolderNameFromArchive(archive, Extensions));

    [Fact]
    public void ExistingFolderIsNeverMergedOrOverwritten()
    {
        string extracted = Extracted("conflict", "mod.ini");
        string character = CharacterFolder("Char", "Pack");
        File.WriteAllText(Path.Combine(character, "Pack", "mod.ini"), "original");

        ArchiveImportPlan plan = ArchiveImportLayout.Plan(extracted, "Pack.zip", Extensions);
        var error = Assert.Throws<ArchiveImportConflictException>(() => ArchiveImportLayout.Install(plan, character));

        Assert.Equal(Path.Combine(character, "Pack"), error.Destination);
        Assert.Equal("original", File.ReadAllText(Path.Combine(character, "Pack", "mod.ini")));
        Assert.Equal(new[] { "Pack" }, Directory.GetDirectories(character).Select(Path.GetFileName));
    }

    [Fact]
    public void CancellationLeavesNoHalfWrittenModOrStagingFolder()
    {
        string extracted = Extracted("cancel", "mod.ini", "b.buf");
        string character = CharacterFolder("Char");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        ArchiveImportPlan plan = ArchiveImportLayout.Plan(extracted, "Pack.zip", Extensions);
        Assert.ThrowsAny<OperationCanceledException>(() => ArchiveImportLayout.Install(plan, character, cancellation.Token));

        Assert.Empty(Directory.GetFileSystemEntries(character));
    }

    [Fact]
    public void InstallRefusesNamesThatEscapeTheCharacterFolder()
    {
        string extracted = Extracted("escape", "mod.ini");
        string character = CharacterFolder("Char");

        var plan = new ArchiveImportPlan(ArchiveLayoutKind.FlatFiles, extracted, Path.Combine("..", "Outside"));

        Assert.ThrowsAny<InvalidDataException>(() => ArchiveImportLayout.Install(plan, character));
        Assert.False(Directory.Exists(Path.Combine(_root, "Outside")));
        Assert.Throws<DirectoryNotFoundException>(() => ArchiveImportLayout.Install(plan, Path.Combine(_root, "missing")));
    }

    [Fact]
    public void HoistMovesASingleTopFolderUpOneLevelKeepingRelativePaths()
    {
        string extracted = Extracted("hoist",
            Path.Combine("Skin", "mod.ini"), Path.Combine("Skin", "Skin", "inner.dds"), Path.Combine("Skin", "res", "a.dds"), Path.Combine("__MACOSX", "x"));

        Assert.True(ArchiveImportLayout.HoistSingleTopFolder(extracted));

        Assert.True(File.Exists(Path.Combine(extracted, "mod.ini")));
        Assert.True(File.Exists(Path.Combine(extracted, "Skin", "inner.dds")));
        Assert.True(File.Exists(Path.Combine(extracted, "res", "a.dds")));
        Assert.DoesNotContain(Directory.GetDirectories(extracted), path => Path.GetFileName(path).StartsWith(".imm-import-", StringComparison.Ordinal));
    }

    [Fact]
    public void HoistChangesNothingForOtherLayoutsOrCollidingClutter()
    {
        string flat = Extracted("hoist-flat", "mod.ini", Path.Combine("Folder", "a.dds"));
        string colliding = Extracted("hoist-collide", Path.Combine("Skin", "Thumbs.db"), "Thumbs.db");
        string empty = Extracted("hoist-empty");
        Directory.CreateDirectory(Path.Combine(empty, "Only"));

        Assert.False(ArchiveImportLayout.HoistSingleTopFolder(flat));
        Assert.False(ArchiveImportLayout.HoistSingleTopFolder(colliding));
        Assert.False(ArchiveImportLayout.HoistSingleTopFolder(empty));
        Assert.True(File.Exists(Path.Combine(flat, "Folder", "a.dds")));
        Assert.True(File.Exists(Path.Combine(colliding, "Skin", "Thumbs.db")));
        Assert.True(Directory.Exists(Path.Combine(empty, "Only")));
    }

    private string Extracted(string name, params string[] files)
    {
        string root = Path.Combine(_root, "extracted-" + name);
        Directory.CreateDirectory(root);
        foreach (string file in files)
        {
            string path = Path.Combine(root, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, file);
        }

        return root;
    }

    private string CharacterFolder(string name, params string[] existingMods)
    {
        string path = Path.Combine(_root, "repository", name);
        Directory.CreateDirectory(path);
        foreach (string mod in existingMods)
        {
            Directory.CreateDirectory(Path.Combine(path, mod));
        }

        return path;
    }
}
