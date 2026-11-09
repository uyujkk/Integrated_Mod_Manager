using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Presets;

namespace IntegratedModManager.Core.Tests;

// Issue #107: a copied folder counts as a Mod's deployment only when the manager recognises it as its own copy.
public sealed class CopyOwnershipTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "imm-ownership-" + Guid.NewGuid().ToString("N"));
    private readonly List<CopyDeploymentRecord> _records = [];

    public CopyOwnershipTests()
    {
        Directory.CreateDirectory(Repository);
        Directory.CreateDirectory(Loader);
    }

    private string Repository => Path.Combine(_root, "Repository");
    private string Loader => Path.Combine(_root, "Loader");
    private CopyDeploymentLedger Ledger => new("repo", Repository, _records);

    public void Dispose()
    {
        DeleteTree(_root);
    }

    // Links first, without following them.
    private static void DeleteTree(string root)
    {
        if (!Directory.Exists(root)) return;
        if (DirectoryLinkDeployment.IsDirectoryLink(root)) { DirectoryLinkDeployment.RemoveLink(root); return; }
        foreach (string folder in Directory.GetDirectories(root)) DeleteTree(folder);
        Directory.Delete(root, recursive: true);
    }

    private string Mod(string relative, params (string Name, string Text)[] files)
    {
        string path = Path.Combine(Repository, relative);
        Directory.CreateDirectory(path);
        foreach (var file in files.Length == 0 ? [("mod.ini", "[TextureOverride]\nhash = 1")] : files)
            File.WriteAllText(Path.Combine(path, file.Name), file.Text);
        return path;
    }

    private static string CopyOf(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        return target;
    }

    [Fact]
    public void Ledger_ARecordedCopyStaysOwnedAfterItIsEdited()
    {
        string source = Mod(@"TestCharacter\TestModA");
        string copy = CopyOf(source, Path.Combine(Loader, "TestModA"));
        Ledger.Record(source, copy);
        File.AppendAllText(Path.Combine(copy, "mod.ini"), "\n; edited by the user");

        Assert.True(Ledger.Owns(source, copy));
    }

    [Fact]
    public void Ledger_ClaimsAnOldIdenticalCopyAndRecordsIt()
    {
        // The migration the user chose (A): no record, same files and sizes.
        string source = Mod(@"TestCharacter\TestModA", ("mod.ini", "a"), ("texture.dds", "bytes"));
        string copy = CopyOf(source, Path.Combine(Loader, "TestModA"));
        CopyDeploymentLedger ledger = Ledger;

        Assert.True(ledger.Owns(source, copy));
        Assert.True(ledger.Changed);
        Assert.Equal(CopyDeploymentOrigin.ClaimedByContent, Assert.Single(_records).Origin);
    }

    [Fact]
    public void Ledger_DoesNotClaimAnEditedOldCopy()
    {
        string source = Mod(@"TestCharacter\TestModA");
        string copy = CopyOf(source, Path.Combine(Loader, "TestModA"));
        File.AppendAllText(Path.Combine(copy, "mod.ini"), "\n; edited");

        Assert.False(Ledger.Owns(source, copy));
        Assert.Empty(_records);
    }

    [Fact]
    public void Ledger_DoesNotClaimACopyWithAnExtraOrMissingFile()
    {
        string source = Mod(@"TestCharacter\TestModA", ("mod.ini", "a"), ("b.ini", "b"));
        string extra = CopyOf(source, Path.Combine(Loader, "Extra"));
        File.WriteAllText(Path.Combine(extra, "notes.txt"), "x");
        string missing = CopyOf(source, Path.Combine(Loader, "Missing"));
        File.Delete(Path.Combine(missing, "b.ini"));

        Assert.False(CopyDeploymentLedger.SameContent(source, extra));
        Assert.False(CopyDeploymentLedger.SameContent(source, missing));
    }

    [Fact]
    public void Ledger_ARecordForAnotherSourceIsNotThisMods()
    {
        string a = Mod(@"CharacterA\Shared");
        string b = Mod(@"CharacterB\Shared", ("mod.ini", "different"));
        string copy = CopyOf(a, Path.Combine(Loader, "Shared"));
        Ledger.Record(a, copy);

        Assert.True(Ledger.Owns(a, copy));
        Assert.False(Ledger.Owns(b, copy));
    }

    [Fact]
    public void Ledger_ForgetsARemovedCopy()
    {
        string source = Mod(@"TestCharacter\TestModA");
        string copy = CopyOf(source, Path.Combine(Loader, "TestModA"));
        Ledger.Record(source, copy);
        Ledger.Forget(copy);
        File.AppendAllText(Path.Combine(copy, "mod.ini"), "\n; changed");

        Assert.Empty(_records);
        Assert.False(Ledger.Owns(source, copy));
    }

    [Fact]
    public void Plan_KeepsAnUnrelatedFolderWithTheSameNameOutOfTheRemoval()
    {
        // The reproduction: TestModA in the loader is the user's own folder; the preset holds only TestModB.
        Mod(@"TestCharacter\TestModA");
        Mod(@"TestCharacter\TestModB");
        string foreign = Path.Combine(Loader, "TestModA");
        Directory.CreateDirectory(foreign);
        File.WriteAllText(Path.Combine(foreign, "foreign-user-data.txt"), "keep");

        ModCombinationDeploymentPlan plan = ModCombinationDeploymentPolicy.Plan(Repository, Loader, [@"TestCharacter\TestModB"], useLinks: false, Ledger.Owns);

        Assert.DoesNotContain(foreign, plan.RemoveTargets, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(foreign, plan.Unowned, StringComparer.OrdinalIgnoreCase);
        Assert.Empty(ModCombinationDeploymentPolicy.Capture(Repository, Loader, Ledger.Owns));
    }

    [Fact]
    public void Plan_RemovesARecordedCopy()
    {
        string a = Mod(@"TestCharacter\TestModA");
        Mod(@"TestCharacter\TestModB");
        string copy = CopyOf(a, Path.Combine(Loader, "TestModA"));
        Ledger.Record(a, copy);
        File.AppendAllText(Path.Combine(copy, "mod.ini"), "\n; edited");

        ModCombinationDeploymentPlan plan = ModCombinationDeploymentPolicy.Plan(Repository, Loader, [@"TestCharacter\TestModB"], useLinks: false, Ledger.Owns);

        Assert.Contains(copy, plan.RemoveTargets, StringComparer.OrdinalIgnoreCase);
        Assert.Empty(plan.Unowned);
    }

    [Fact]
    public void Capture_ARecordPicksOneOfTwoSourcesWithTheSameName()
    {
        // Same-named sources: the record says whose copy it is, instead of guessing or failing.
        string a = Mod(@"CharacterA\Shared");
        Mod(@"CharacterB\Shared", ("mod.ini", "different"));
        Ledger.Record(a, CopyOf(a, Path.Combine(Loader, "Shared")));

        Assert.Equal([@"CharacterA\Shared"], ModCombinationDeploymentPolicy.Capture(Repository, Loader, Ledger.Owns));
    }

    [Fact]
    public void Plan_AnExternalJunctionIsNeitherOwnedNorRemoved()
    {
        Mod(@"TestCharacter\TestModB");
        string outside = Path.Combine(_root, "Outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "keep.txt"), "keep");
        Mod(@"TestCharacter\TestModA");
        DirectoryLinkDeployment.CreateJunction(Path.Combine(Loader, "TestModA"), outside);

        ModCombinationDeploymentPlan plan = ModCombinationDeploymentPolicy.Plan(Repository, Loader, [@"TestCharacter\TestModB"], useLinks: false, Ledger.Owns);

        Assert.Empty(plan.RemoveTargets);
        Assert.True(File.Exists(Path.Combine(outside, "keep.txt")));
    }

    [Fact]
    public void Plan_AnExternalFolderInTheWayIsNotInstalledOver()
    {
        Mod(@"TestCharacter\TestModA");
        string foreign = Path.Combine(Loader, "TestModA");
        Directory.CreateDirectory(foreign);
        File.WriteAllText(Path.Combine(foreign, "foreign-user-data.txt"), "keep");

        ModCombinationDeploymentPlan plan = ModCombinationDeploymentPolicy.Plan(Repository, Loader, [@"TestCharacter\TestModA"], useLinks: false, Ledger.Owns);

        Assert.Empty(plan.InstallSources);
        Assert.Contains(foreign, plan.Unowned, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Preview_ShowsTheExternalFolderAndKeepsIt()
    {
        Mod(@"TestCharacter\TestModA");
        Mod(@"TestCharacter\TestModB");
        string foreign = Path.Combine(Loader, "TestModA");
        Directory.CreateDirectory(foreign);
        File.WriteAllText(Path.Combine(foreign, "foreign-user-data.txt"), "keep");
        var request = new CombinationRestoreRequest(Repository, Loader, [@"TestCharacter\TestModB"], false, false, [], string.Empty, Ledger.Owns);

        CombinationRestorePreview preview = CombinationRestorePreviewService.Prepare(request);

        Assert.True(preview.CanRestore);
        Assert.Contains(preview.Mods, mod => mod.Action == CombinationModAction.UnownedCopy && mod.Path.Equals(foreign, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(preview.Mods, mod => mod.Action == CombinationModAction.Remove);
    }

    [Fact]
    public void Preview_BlocksTheRestoreWhenAnExternalFolderIsInTheWay()
    {
        Mod(@"TestCharacter\TestModA");
        Directory.CreateDirectory(Path.Combine(Loader, "TestModA"));
        File.WriteAllText(Path.Combine(Loader, "TestModA", "foreign-user-data.txt"), "keep");
        var request = new CombinationRestoreRequest(Repository, Loader, [@"TestCharacter\TestModA"], false, false, [], string.Empty, Ledger.Owns);

        CombinationRestorePreview preview = CombinationRestorePreviewService.Prepare(request);

        Assert.False(preview.CanRestore);
        Assert.Contains(preview.Issues, issue => issue.Code == CombinationPreviewIssueCode.UnownedCopyInTheWay);
    }

    [Fact]
    public void Preview_AfterTakingOverTheFolderIsTheModsDeployment()
    {
        string a = Mod(@"TestCharacter\TestModA");
        Mod(@"TestCharacter\TestModB");
        string foreign = Path.Combine(Loader, "TestModA");
        Directory.CreateDirectory(foreign);
        File.WriteAllText(Path.Combine(foreign, "edited-copy.txt"), "x");
        Ledger.Adopt(a, foreign);
        var request = new CombinationRestoreRequest(Repository, Loader, [@"TestCharacter\TestModB"], false, false, [], string.Empty, Ledger.Owns);

        CombinationRestorePreview preview = CombinationRestorePreviewService.Prepare(request);

        Assert.Contains(preview.Mods, mod => mod.Action == CombinationModAction.Remove && mod.Path.Equals(foreign, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(CopyDeploymentOrigin.Adopted, Assert.Single(_records).Origin);
    }
}
