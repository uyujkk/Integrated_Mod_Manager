using System.Text.Json;
using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Infrastructure;

namespace IntegratedModManager.Core.Tests;

public sealed class ExistingModImportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "imm-import-tests-" + Guid.NewGuid().ToString("N"));
    private string Source => Path.Combine(_root, "old-library");
    private string Target => Path.Combine(_root, "repository", "角色");
    private string Manifest => Path.Combine(_root, "records", "import.json");
    public ExistingModImportTests() { Directory.CreateDirectory(Source); Directory.CreateDirectory(Target); }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private string Mod(string name, string content = "global persist $variant = 1")
    {
        string root = Path.Combine(Source, name);
        Directory.CreateDirectory(Path.Combine(root, "textures"));
        File.WriteAllText(Path.Combine(root, "mod.ini"), content);
        File.WriteAllText(Path.Combine(root, "textures", "fixture.txt"), "synthetic texture fixture");
        return root;
    }

    [Fact]
    public async Task PreviewCopyAndUndo_PreserveSourcesAndTrackNestedFiles()
    {
        string original = Mod("测试 Mod");
        Directory.CreateDirectory(Path.Combine(original, "empty"));
        var plan = ExistingModImportService.Preview(Source, Target);
        Assert.Single(plan.Items);
        Assert.False(Directory.Exists(Path.Combine(Target, "测试 Mod")));
        await ExistingModImportService.ExecuteAsync(plan, Manifest);
        Assert.True(File.Exists(Path.Combine(original, "mod.ini")));
        Assert.Equal(File.ReadAllText(Path.Combine(original, "mod.ini")), File.ReadAllText(Path.Combine(Target, "测试 Mod", "mod.ini")));
        var undone = ExistingModImportService.Undo(Manifest, Target);
        Assert.Equal(["测试 Mod"], undone.Removed); Assert.Empty(undone.Preserved);
        Assert.False(Directory.Exists(Path.Combine(Target, "测试 Mod")));
        Assert.True(Directory.Exists(original));
        Assert.Empty(ExistingModImportService.Undo(Manifest, Target).Removed);
    }

    [Fact]
    public async Task ConflictingDestination_BlocksEntirePreviewWithoutOverwriting()
    {
        Mod("A"); Mod("B");
        Directory.CreateDirectory(Path.Combine(Target, "B"));
        File.WriteAllText(Path.Combine(Target, "B", "keep.txt"), "existing");
        var plan = ExistingModImportService.Preview(Source, Target);
        Assert.NotNull(plan.Items.Single(item => item.SourceName == "B").Conflict);
        await Assert.ThrowsAsync<InvalidDataException>(() => ExistingModImportService.ExecuteAsync(plan, Manifest));
        Assert.False(Directory.Exists(Path.Combine(Target, "A")));
        Assert.Equal("existing", File.ReadAllText(Path.Combine(Target, "B", "keep.txt")));
    }

    [Fact]
    public async Task SourceChangedAfterPreview_RejectsAllWrites()
    {
        string original = Mod("A");
        var plan = ExistingModImportService.Preview(Source, Target);
        File.WriteAllText(Path.Combine(original, "mod.ini"), "changed");
        await Assert.ThrowsAsync<IOException>(() => ExistingModImportService.ExecuteAsync(plan, Manifest));
        Assert.Empty(Directory.GetDirectories(Target)); Assert.False(File.Exists(Manifest));
    }

    [Fact]
    public async Task DestinationAppearingAfterPreview_IsNotOverwritten()
    {
        Mod("A");
        var plan = ExistingModImportService.Preview(Source, Target);
        Directory.CreateDirectory(Path.Combine(Target, "A"));
        await Assert.ThrowsAsync<IOException>(() => ExistingModImportService.ExecuteAsync(plan, Manifest));
        Assert.Empty(Directory.GetFiles(Path.Combine(Target, "A")));
    }

    [Theory]
    [InlineData("file")]
    [InlineData("extra-file")]
    [InlineData("extra-directory")]
    public async Task UndoPreservesModifiedCopies_AndRemovesUnchangedSiblings(string modification)
    {
        Mod("A"); Mod("B");
        await ExistingModImportService.ExecuteAsync(ExistingModImportService.Preview(Source, Target), Manifest);
        string changed = Path.Combine(Target, "A");
        if (modification == "file") File.WriteAllText(Path.Combine(changed, "mod.ini"), "user edited");
        else if (modification == "extra-file") File.WriteAllText(Path.Combine(changed, "new.txt"), "user file");
        else Directory.CreateDirectory(Path.Combine(changed, "user folder"));
        var result = ExistingModImportService.Undo(Manifest, Target);
        Assert.Equal(["A"], result.Preserved); Assert.Equal(["B"], result.Removed);
        Assert.True(Directory.Exists(changed)); Assert.True(Directory.Exists(Path.Combine(Source, "B")));
    }

    [Fact]
    public async Task PreCanceledImport_ProducesNoOutput()
    {
        Mod("A");
        var plan = ExistingModImportService.Preview(Source, Target);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ExistingModImportService.ExecuteAsync(plan, Manifest, cancellationToken: new CancellationToken(true)));
        Assert.Empty(Directory.GetDirectories(Target));
    }

    [Fact]
    public void OverlappingTrees_AreRejected()
    {
        Mod("A");
        Assert.Throws<InvalidDataException>(() => ExistingModImportService.Preview(Source, Source));
        Assert.Throws<InvalidDataException>(() => ExistingModImportService.Preview(Source, Path.Combine(Source, "A")));
    }

    [Fact]
    public async Task CancelAfterOneCopy_RecordsOnlyOwnedOutput_AndCanUndo()
    {
        Mod("A"); Mod("B");
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ExistingModImportService.ExecuteAsync(
            ExistingModImportService.Preview(Source, Target), Manifest, (_, _) => cancellation.Cancel(), cancellation.Token));
        Assert.True(Directory.Exists(Path.Combine(Target, "A")));
        Assert.False(Directory.Exists(Path.Combine(Target, "B")));
        Assert.Single(ExistingModImportService.Undo(Manifest, Target).Removed);
        Assert.Equal(2, Directory.GetDirectories(Source).Length);
    }

    [Fact]
    public async Task UndoRejectsWrongDestinationAndTraversalRecord()
    {
        Mod("A");
        await ExistingModImportService.ExecuteAsync(ExistingModImportService.Preview(Source, Target), Manifest);
        Assert.Throws<InvalidDataException>(() => ExistingModImportService.Undo(Manifest, Source));
        var record = JsonSerializer.Deserialize<ExistingModImportManifest>(File.ReadAllText(Manifest))!;
        record.Outputs[0] = record.Outputs[0] with { DestinationName = "../outside" };
        File.WriteAllText(Manifest, JsonSerializer.Serialize(record));
        Assert.Throws<InvalidDataException>(() => ExistingModImportService.Undo(Manifest, Target));
        Assert.True(Directory.Exists(Path.Combine(Target, "A")));
    }

    [Fact]
    public void NonModFolders_AreExcludedFromPreview()
    {
        Directory.CreateDirectory(Path.Combine(Source, "notes"));
        File.WriteAllText(Path.Combine(Source, "notes", "readme.txt"), "notes");
        Assert.Empty(ExistingModImportService.Preview(Source, Target).Items);
    }

    [Fact]
    public async Task PublicationFailureHasDurableOwnershipIntentAndCanBeRecoveredAndUndone()
    {
        Mod("A");
        await Assert.ThrowsAsync<IOException>(() => ExistingModImportService.ExecuteAsync(
            ExistingModImportService.Preview(Source, Target), Manifest, afterPublication: () => throw new IOException("Simulated termination after rename")));
        var before = RecoveryJsonFile.Read<ExistingModImportManifest>(Manifest);
        Assert.Empty(before.Outputs); Assert.Single(before.Pending);
        Assert.True(File.Exists(Path.Combine(Target, "A", ExistingModImportService.OwnershipFileName)));
        var recovered = ExistingModImportService.Recover(Manifest, Target);
        Assert.Equal(["A"], recovered.Recovered); Assert.Empty(recovered.Preserved);
        Assert.Single(ExistingModImportService.Undo(Manifest, Target).Removed);
        Assert.True(File.Exists(Path.Combine(Source, "A", "mod.ini")));
    }

    [Theory]
    [InlineData("edited")]
    [InlineData("missing-marker")]
    [InlineData("wrong-token")]
    public async Task RecoveryPreservesChangedOrUnprovenPublications(string scenario)
    {
        Mod("A");
        await Assert.ThrowsAsync<IOException>(() => ExistingModImportService.ExecuteAsync(
            ExistingModImportService.Preview(Source, Target), Manifest, afterPublication: () => throw new IOException("Publication fault")));
        string directory = Path.Combine(Target, "A");
        string marker = Path.Combine(directory, ExistingModImportService.OwnershipFileName);
        if (scenario == "edited") File.WriteAllText(Path.Combine(directory, "mod.ini"), "user modification");
        else if (scenario == "missing-marker") File.Delete(marker);
        else RecoveryJsonFile.Write(marker, Guid.NewGuid().ToString("N"), keepBackup: false);
        var recovered = ExistingModImportService.Recover(Manifest, Target);
        Assert.Empty(recovered.Recovered); Assert.Equal(["A"], recovered.Preserved);
        Assert.Empty(ExistingModImportService.Undo(Manifest, Target).Removed);
        Assert.True(Directory.Exists(directory));
    }

    [Fact]
    public async Task ActiveImportRecordCannotBeRecoveredByASecondOperation()
    {
        Mod("A");
        await ExistingModImportService.ExecuteAsync(ExistingModImportService.Preview(Source, Target), Manifest,
            afterPublication: () => Assert.Throws<IOException>(() => ExistingModImportService.Recover(Manifest, Target)));
        Assert.Single(ExistingModImportService.Undo(Manifest, Target).Removed);
    }

    [Fact]
    public async Task UserAddedOwnershipFileIsPreservedAndNeverTreatedAsApplicationMetadata()
    {
        Mod("A");
        await ExistingModImportService.ExecuteAsync(ExistingModImportService.Preview(Source, Target), Manifest);
        string marker = Path.Combine(Target, "A", ExistingModImportService.OwnershipFileName);
        File.WriteAllText(marker, "user file");
        Assert.Contains("A", ExistingModImportService.Recover(Manifest, Target).Preserved);
        Assert.Empty(ExistingModImportService.Undo(Manifest, Target).Removed);
        Assert.Equal("user file", File.ReadAllText(marker));
    }

    [Fact]
    public async Task LegacyVersionOneRecordStillUndoesUnchangedCopies()
    {
        Mod("A");
        await ExistingModImportService.ExecuteAsync(ExistingModImportService.Preview(Source, Target), Manifest);
        var legacy = RecoveryJsonFile.Read<ExistingModImportManifest>(Manifest);
        legacy.Version = 1; legacy.Outputs[0] = legacy.Outputs[0] with { OwnershipToken = null };
        RecoveryJsonFile.Write(Manifest, legacy);
        Assert.Single(ExistingModImportService.Undo(Manifest, Target).Removed);
    }
}
