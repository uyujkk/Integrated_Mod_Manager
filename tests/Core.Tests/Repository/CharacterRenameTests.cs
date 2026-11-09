using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Repository;

namespace IntegratedModManager.Core.Tests;

// Issue #108: renaming a character moves its links and saved paths with it, or leaves everything as it was.
public sealed class CharacterRenameTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "imm-rename-" + Guid.NewGuid().ToString("N"));

    public CharacterRenameTests()
    {
        Directory.CreateDirectory(Path.Combine(Repository, "TestCharacter", "TestModA"));
        File.WriteAllText(Path.Combine(Repository, "TestCharacter", "TestModA", "mod.ini"), "[TextureOverride]");
        Directory.CreateDirectory(Path.Combine(Repository, "TestCharacter", "TestModB"));
        Directory.CreateDirectory(Path.Combine(Repository, "OtherCharacter", "OtherMod"));
        Directory.CreateDirectory(Loader);
    }

    private string Repository => Path.Combine(_root, "Repository");
    private string Loader => Path.Combine(_root, "Loader");
    private string OldCharacter => Path.Combine(Repository, "TestCharacter");
    private string NewCharacter => Path.Combine(Repository, "RenamedCharacter");

    public void Dispose()
    {
        DeleteTree(_root);
    }

    private static void DeleteTree(string root)
    {
        if (!Directory.Exists(root)) return;
        if (DirectoryLinkDeployment.IsDirectoryLink(root)) { DirectoryLinkDeployment.RemoveLink(root); return; }
        foreach (string folder in Directory.GetDirectories(root)) DeleteTree(folder);
        Directory.Delete(root, recursive: true);
    }

    private string Link(string name, string target)
    {
        string link = Path.Combine(Loader, name);
        DirectoryLinkDeployment.CreateJunction(link, target);
        return link;
    }

    // The saved data of the issue: a preset with "TestCharacter\TestModA", plus a full-path key.
    private sealed class FakeReferences : ICharacterRenameReferences
    {
        private readonly ReferenceUndoLog _log = new();
        public List<string> PresetPaths { get; } = [@"TestCharacter\TestModA", @"OtherCharacter\OtherMod"];
        public Dictionary<string, string> Links { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Exception? SaveError { get; set; }
        public int SaveFailures { get; set; } = int.MaxValue;
        public int Saves { get; private set; }
        public List<string> SavedPresetPaths { get; private set; } = [];
        public List<CopyDeploymentRecord> CopyRecords { get; } = [];

        public void Migrate(CharacterRenamePlan plan)
        {
            _log.RewriteAll(PresetPaths, plan.MapRelative);
            _log.Rekey(Links, plan.MapFullPath);
            CopyDeploymentLedger.RenameCharacter(CopyRecords, "repo", plan, _log);
        }

        public void Revert() => _log.Undo();

        public void Save()
        {
            Saves++;
            if (SaveError is not null && SaveFailures-- > 0) throw SaveError;
            SavedPresetPaths = [.. PresetPaths];
        }
    }

    [Fact]
    public void Execute_MovesTheJunctionAndThePresetPathWithTheCharacter()
    {
        // The issue's reproduction, checking the fixed result.
        string link = Link("TestModA", Path.Combine(OldCharacter, "TestModA"));
        var references = new FakeReferences();
        references.Links[Path.Combine(OldCharacter, "TestModA")] = "https://example.invalid/mod";

        CharacterRenamePlan plan = CharacterRename.Prepare(OldCharacter, "RenamedCharacter", Loader);
        CharacterRename.Execute(plan, references);

        Assert.True(Directory.Exists(Path.Combine(NewCharacter, "TestModA")));
        Assert.False(Directory.Exists(OldCharacter));
        Assert.Equal(Path.Combine(NewCharacter, "TestModA"), DirectoryLinkDeployment.TryGetStoredTarget(link), StringComparer.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(link, "mod.ini")));
        Assert.Equal([@"RenamedCharacter\TestModA", @"OtherCharacter\OtherMod"], references.SavedPresetPaths);
        Assert.True(references.Links.ContainsKey(Path.Combine(NewCharacter, "TestModA")));
    }

    // Copy-mode records (#107) of the repository; only those of the renamed character change.
    private void AddCopyRecords(FakeReferences references)
    {
        CopyDeploymentRecord Record(string repository, string source, string deployed)
            => new() { RepositoryId = repository, SourceRelativePath = source, DeployedPath = Path.Combine(Loader, deployed) };
        references.CopyRecords.AddRange(
        [
            Record("repo", @"TestCharacter\TestModB", "TestModB"),
            Record("repo", @"OtherCharacter\OtherMod", "OtherMod"),
            Record("repo", @"TestCharacterTwo\TestModB", "TestModB2"),
            Record("other-repo", @"TestCharacter\TestModB", "Elsewhere")
        ]);
    }

    private static string[] Sources(FakeReferences references) => [.. references.CopyRecords.Select(record => record.RepositoryId + ":" + record.SourceRelativePath)];

    [Fact]
    public void Execute_MovesTheCopyRecordsOfThisCharacterOnly()
    {
        var references = new FakeReferences();
        AddCopyRecords(references);

        CharacterRename.Execute(CharacterRename.Prepare(OldCharacter, "RenamedCharacter", Loader), references);

        Assert.Equal(
        [
            @"repo:RenamedCharacter\TestModB", @"repo:OtherCharacter\OtherMod",
            @"repo:TestCharacterTwo\TestModB", @"other-repo:TestCharacter\TestModB"
        ], Sources(references));
        // The copies themselves stay where they are in the loader folder.
        Assert.Equal(Path.Combine(Loader, "TestModB"), references.CopyRecords[0].DeployedPath);
    }

    [Fact]
    public void Execute_AFailedRenamePutsTheCopyRecordsBack()
    {
        var references = new FakeReferences { SaveError = new IOException("disk full"), SaveFailures = 1 };
        AddCopyRecords(references);
        string[] before = Sources(references);

        var error = Assert.Throws<CharacterRenameException>(() =>
            CharacterRename.Execute(CharacterRename.Prepare(OldCharacter, "RenamedCharacter", Loader), references));

        Assert.True(error.RolledBack);
        Assert.Equal(before, Sources(references));
        Assert.True(Directory.Exists(OldCharacter));
    }

    [Fact]
    public void CopyRecords_ACopyInsideTheCharacterFolderMovesWithIt()
    {
        CharacterRenamePlan plan = CharacterRename.Prepare(OldCharacter, "RenamedCharacter", null);
        var record = new CopyDeploymentRecord { RepositoryId = "repo", SourceRelativePath = @"TestCharacter\TestModA", DeployedPath = Path.Combine(OldCharacter, "Deployed") };
        var log = new ReferenceUndoLog();

        CopyDeploymentLedger.RenameCharacter([record], "repo", plan, log);

        Assert.Equal(Path.Combine(NewCharacter, "Deployed"), record.DeployedPath);
        log.Undo();
        Assert.Equal(Path.Combine(OldCharacter, "Deployed"), record.DeployedPath);
        Assert.Equal(@"TestCharacter\TestModA", record.SourceRelativePath);
    }

    [Fact]
    public void Prepare_FindsOnlyLinksIntoTheCharacter()
    {
        Link("TestModA", Path.Combine(OldCharacter, "TestModA"));
        Link("Whole", OldCharacter);
        Link("Other", Path.Combine(Repository, "OtherCharacter", "OtherMod"));
        Directory.CreateDirectory(Path.Combine(Loader, "PlainCopy"));

        CharacterRenamePlan plan = CharacterRename.Prepare(OldCharacter, "RenamedCharacter", Loader);

        Assert.True(plan.CanRename);
        Assert.Equal(["TestModA", "Whole"], plan.Links.Select(link => Path.GetFileName(link.LinkPath)));
        Assert.Equal(NewCharacter, plan.Links.Single(link => link.LinkPath.EndsWith("Whole")).NewTarget, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Prepare_BlocksANameThatIsTaken()
    {
        CharacterRenamePlan plan = CharacterRename.Prepare(OldCharacter, "OtherCharacter", Loader);

        Assert.Equal(CharacterRenameBlock.NameTaken, plan.Block);
        Assert.Throws<InvalidOperationException>(() => CharacterRename.Execute(plan, new FakeReferences()));
        Assert.True(Directory.Exists(OldCharacter));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("..")]
    [InlineData("bad|name")]
    [InlineData("trailing.")]
    public void Prepare_BlocksAnInvalidName(string name)
    {
        Assert.Equal(CharacterRenameBlock.InvalidName, CharacterRename.Prepare(OldCharacter, name, Loader).Block);
    }

    [Fact]
    public void Prepare_BlocksAMissingFolder()
    {
        Assert.Equal(CharacterRenameBlock.SourceMissing, CharacterRename.Prepare(Path.Combine(Repository, "Gone"), "New", Loader).Block);
    }

    [Fact]
    public void Prepare_BlocksWhenTheLoaderFolderIsUnavailable()
    {
        Assert.Equal(CharacterRenameBlock.LoaderUnavailable, CharacterRename.Prepare(OldCharacter, "RenamedCharacter", Path.Combine(_root, "Unplugged")).Block);
    }

    [Fact]
    public void Prepare_WithoutALoaderFolderHasNoLinks()
    {
        CharacterRenamePlan plan = CharacterRename.Prepare(OldCharacter, "RenamedCharacter", null);

        Assert.True(plan.CanRename);
        Assert.Empty(plan.Links);
    }

    [Fact]
    public void Execute_ASaveFailureLeavesTheFolderJunctionAndReferencesAsTheyWere()
    {
        string link = Link("TestModA", Path.Combine(OldCharacter, "TestModA"));
        var references = new FakeReferences { SaveError = new IOException("disk full"), SaveFailures = 1 };
        references.Links[Path.Combine(OldCharacter, "TestModA")] = "x";
        CharacterRenamePlan plan = CharacterRename.Prepare(OldCharacter, "RenamedCharacter", Loader);

        var error = Assert.Throws<CharacterRenameException>(() => CharacterRename.Execute(plan, references));

        Assert.True(error.RolledBack);
        Assert.IsType<IOException>(error.InnerException);
        Assert.True(Directory.Exists(Path.Combine(OldCharacter, "TestModA")));
        Assert.False(Directory.Exists(NewCharacter));
        Assert.Equal(Path.Combine(OldCharacter, "TestModA"), DirectoryLinkDeployment.TryGetStoredTarget(link), StringComparer.OrdinalIgnoreCase);
        Assert.Equal([@"TestCharacter\TestModA", @"OtherCharacter\OtherMod"], references.PresetPaths);
        Assert.True(references.Links.ContainsKey(Path.Combine(OldCharacter, "TestModA")));
        // Saved again as it was, so the files on disk match the folders.
        Assert.Equal(2, references.Saves);
        Assert.Equal([@"TestCharacter\TestModA", @"OtherCharacter\OtherMod"], references.SavedPresetPaths);
    }

    [Fact]
    public void Execute_ReportsWhatCouldNotBeRestored()
    {
        Link("TestModA", Path.Combine(OldCharacter, "TestModA"));
        var references = new FakeReferences { SaveError = new IOException("read-only") };
        CharacterRenamePlan plan = CharacterRename.Prepare(OldCharacter, "RenamedCharacter", Loader);

        var error = Assert.Throws<CharacterRenameException>(() => CharacterRename.Execute(plan, references));

        Assert.False(error.RolledBack);
        Assert.Contains(error.Unrestored, item => item.StartsWith("saved references", StringComparison.Ordinal));
        Assert.True(Directory.Exists(OldCharacter));
    }

    [Fact]
    public void Execute_AFolderInUseLeavesTheLinksInPlace()
    {
        string link = Link("TestModA", Path.Combine(OldCharacter, "TestModA"));
        CharacterRenamePlan plan = CharacterRename.Prepare(OldCharacter, "RenamedCharacter", Loader);
        var references = new FakeReferences();

        using (File.Open(Path.Combine(OldCharacter, "TestModA", "mod.ini"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var error = Assert.Throws<CharacterRenameException>(() => CharacterRename.Execute(plan, references));
            Assert.True(error.RolledBack);
        }

        Assert.Equal(Path.Combine(OldCharacter, "TestModA"), DirectoryLinkDeployment.TryGetStoredTarget(link), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(0, references.Saves);
        Assert.Equal([@"TestCharacter\TestModA", @"OtherCharacter\OtherMod"], references.PresetPaths);
    }

    [Fact]
    public void Execute_ALinkThatCannotBeRebuiltUndoesTheMove()
    {
        string first = Link("A1", Path.Combine(OldCharacter, "TestModA"));
        string second = Link("A2", Path.Combine(OldCharacter, "TestModB"));
        CharacterRenamePlan plan = CharacterRename.Prepare(OldCharacter, "RenamedCharacter", Loader);
        var references = new FakeReferences();
        int created = 0;
        void Create(string link, string target)
        {
            // The second link to the new path cannot be made (the loader folder became inaccessible, say).
            if (++created == 2 && target.StartsWith(NewCharacter, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("denied");
            DirectoryLinkDeployment.CreateJunction(link, target);
        }

        var error = Assert.Throws<CharacterRenameException>(() => CharacterRename.Execute(plan, references, Create));

        Assert.True(error.RolledBack);
        Assert.IsType<UnauthorizedAccessException>(error.InnerException);
        Assert.False(Directory.Exists(NewCharacter));
        Assert.Equal(Path.Combine(OldCharacter, "TestModA"), DirectoryLinkDeployment.TryGetStoredTarget(first), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(Path.Combine(OldCharacter, "TestModB"), DirectoryLinkDeployment.TryGetStoredTarget(second), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(0, references.Saves);
    }

    [Fact]
    public void Execute_AMigrationFailurePutsTheLinksAndFolderBack()
    {
        string link = Link("TestModA", Path.Combine(OldCharacter, "TestModA"));
        CharacterRenamePlan plan = CharacterRename.Prepare(OldCharacter, "RenamedCharacter", Loader);
        var references = new FailingMigration();

        var error = Assert.Throws<CharacterRenameException>(() => CharacterRename.Execute(plan, references));

        Assert.True(error.RolledBack);
        Assert.True(Directory.Exists(OldCharacter));
        Assert.Equal(Path.Combine(OldCharacter, "TestModA"), DirectoryLinkDeployment.TryGetStoredTarget(link), StringComparer.OrdinalIgnoreCase);
        Assert.False(references.Saved);
    }

    private sealed class FailingMigration : ICharacterRenameReferences
    {
        public bool Saved { get; private set; }
        public void Migrate(CharacterRenamePlan plan) => throw new InvalidDataException("broken settings");
        public void Revert() { }
        public void Save() => Saved = true;
    }

    [Fact]
    public void Prepare_BlocksWhenSomeLinksCannotBeRead()
    {
        // Partly inaccessible links: one may point into the character, so nothing moves and the user is told which.
        string readable = Link("TestModA", Path.Combine(OldCharacter, "TestModA"));
        string unreadable = Link("Locked", Path.Combine(Repository, "OtherCharacter", "OtherMod"));

        CharacterRenamePlan plan = CharacterRename.Prepare(OldCharacter, "RenamedCharacter", Loader,
            link => link.EndsWith("Locked", StringComparison.OrdinalIgnoreCase) ? null : DirectoryLinkDeployment.TryGetStoredTarget(link));

        Assert.Equal(CharacterRenameBlock.LinksUnreadable, plan.Block);
        Assert.Equal([unreadable], plan.UnreadableLinks, StringComparer.OrdinalIgnoreCase);
        Assert.Throws<InvalidOperationException>(() => CharacterRename.Execute(plan, new FakeReferences()));
        Assert.True(Directory.Exists(OldCharacter));
        Assert.Equal(Path.Combine(OldCharacter, "TestModA"), DirectoryLinkDeployment.TryGetStoredTarget(readable), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plan_MapsPathsAndNamesOfTheCharacterOnly()
    {
        CharacterRenamePlan plan = CharacterRename.Prepare(OldCharacter, "RenamedCharacter", null);

        Assert.Equal(@"RenamedCharacter\TestModA", plan.MapRelative(@"TestCharacter\TestModA"));
        Assert.Equal("RenamedCharacter/TestModA", plan.MapRelative("testcharacter/TestModA"));
        Assert.Equal("RenamedCharacter", plan.MapRelative("TestCharacter"));
        Assert.Null(plan.MapRelative(@"TestCharacterTwo\Mod"));
        Assert.Null(plan.MapRelative(@"OtherCharacter\TestCharacter"));
        Assert.Equal(Path.Combine(NewCharacter, "TestModA"), plan.MapFullPath(Path.Combine(OldCharacter, "TestModA") + Path.DirectorySeparatorChar));
        Assert.Null(plan.MapFullPath(OldCharacter + "Two"));
        Assert.Null(plan.MapFullPath(""));
        Assert.Equal("RenamedCharacter", plan.MapName("testCHARACTER"));
        Assert.Null(plan.MapName("OtherCharacter"));
    }

    [Fact]
    public void UndoLog_PutsEverythingBack()
    {
        CharacterRenamePlan plan = CharacterRename.Prepare(OldCharacter, "RenamedCharacter", null);
        var log = new ReferenceUndoLog();
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["TestCharacter"] = 1, ["RenamedCharacter"] = 9, ["Other"] = 2 };
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "TestCharacter/A", "Other/B" };
        string value = @"TestCharacter\TestModA";

        log.Rekey(map, plan.MapName);
        log.RewriteAll(set, plan.MapRelative);
        log.Rewrite(() => value, next => value = next, plan.MapRelative);
        log.Rewrite(() => value, next => value = next, _ => null);
        log.Add(() => { });

        Assert.Equal(1, map["RenamedCharacter"]);
        Assert.False(map.ContainsKey("TestCharacter"));
        Assert.Contains("RenamedCharacter/A", set);
        Assert.Equal(@"RenamedCharacter\TestModA", value);
        Assert.Equal(4, log.Count);

        log.Undo();

        Assert.Equal(1, map["TestCharacter"]);
        Assert.Equal(9, map["RenamedCharacter"]);
        Assert.Equal(["Other/B", "TestCharacter/A"], set.Order(StringComparer.Ordinal));
        Assert.Equal(@"TestCharacter\TestModA", value);
        Assert.Equal(0, log.Count);
    }
}
