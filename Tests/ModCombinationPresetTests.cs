using System.Text.Json;
using IntegratedModManager.Core;

namespace IntegratedModManager.Core.Tests;

public sealed class ModCombinationPresetTests
{
    [Fact]
    public void CaptureRestoreTwoMods_UsesOnePatchAndPreservesOtherModsAndSourceIni()
    {
        using var f = new Fixture();
        string initial = f.State("1", "2");
        var captured = ModPersistentPresetEngine.CaptureCombination(f.Mods, f.User);
        string serialized = JsonSerializer.Serialize(captured);
        captured = JsonSerializer.Deserialize<List<ModPersistentSourceState>>(serialized)!;
        Assert.Equal(2, captured.Count);
        Assert.Equal("1", Assert.Single(captured[0].Values).Value);
        Assert.Equal("2", Assert.Single(captured[1].Values).Value);
        string other = f.State("9", "8");
        var plan = ModPersistentPresetEngine.PlanCombinationRuntimeRestore(captured, f.User);
        Assert.Equal(2, plan.ChangedCount);
        string backup = Assert.IsType<string>(ModPersistentPresetEngine.ApplyRuntimeRestore(plan));
        Assert.Equal(other, File.ReadAllText(backup));
        Assert.Equal(initial, File.ReadAllText(f.User));
        Assert.Single(Directory.GetFiles(f.Root, "*.bak"));
        Assert.Equal("[Constants]\nglobal persist $x = 0\n", File.ReadAllText(Path.Combine(f.A, "mod.ini")));
        Assert.Equal("[Constants]\nglobal persist $x = 0\n", File.ReadAllText(Path.Combine(f.B, "mod.ini")));
        Assert.Null(ModPersistentPresetEngine.ApplyRuntimeRestore(ModPersistentPresetEngine.PlanCombinationRuntimeRestore(captured, f.User)));
    }

    [Fact]
    public void OnlySavedCombinationKeysAreRestored_EvenWhenLoaderStoresAllMods()
    {
        using var f = new Fixture();
        f.State("1", "2");
        var one = ModPersistentPresetEngine.CaptureCombination([f.Mods[0]], f.User);
        string current = f.State("9", "8");
        ModPersistentPresetEngine.ApplyRuntimeRestore(ModPersistentPresetEngine.PlanCombinationRuntimeRestore(one, f.User));
        Assert.Equal(current.Replace("\\A\\mod.ini\\x = 9", "\\A\\mod.ini\\x = 1"), File.ReadAllText(f.User));
    }

    [Fact]
    public void SameLocalVariableNameInDifferentNamespacesDoesNotCollide()
    {
        using var f = new Fixture();
        f.State("-1", "0.519999981");
        var captured = ModPersistentPresetEngine.CaptureCombination(f.Mods, f.User);
        Assert.Equal("$x", captured[0].Values[0].Variable);
        Assert.Equal("$x", captured[1].Values[0].Variable);
        Assert.NotEqual(captured[0].Values[0].RuntimeKey, captured[1].Values[0].RuntimeKey);
        Assert.Equal("0.519999981", captured[1].Values[0].Value);
    }

    [Fact]
    public void MissingRuntimeValueNeverFallsBackToModDefault()
    {
        using var f = new Fixture();
        File.WriteAllText(f.User, "[Constants]\n$\\Mods\\A\\mod.ini\\x=1\n");
        Assert.Throws<InvalidDataException>(() => ModPersistentPresetEngine.CaptureCombination(f.Mods, f.User));
        Assert.Empty(Directory.GetFiles(f.Root, "*.bak"));
    }

    [Fact]
    public void DuplicateLoaderAssignmentRejectsCombinationCapture()
    {
        using var f = new Fixture();
        f.State("1", "2");
        File.AppendAllText(f.User, "$\\Mods\\A\\mod.ini\\x=4\n");
        Assert.Throws<InvalidDataException>(() => ModPersistentPresetEngine.CaptureCombination(f.Mods, f.User));
    }

    [Fact]
    public void ExplicitNamespaceCollisionAcrossModsRejectsCaptureAndRestore()
    {
        using var f = new Fixture();
        foreach (var mod in f.Mods) File.WriteAllText(Path.Combine(mod.ModDirectory, "mod.ini"), "namespace=Shared\n[Constants]\nglobal persist $x=0\n");
        File.WriteAllText(f.User, "[Constants]\n$\\Shared\\x=2\n");
        Assert.Throws<InvalidDataException>(() => ModPersistentPresetEngine.CaptureCombination(f.Mods, f.User));
        var value = new ModPersistentValue("mod.ini", "$x", "$\\shared\\x", "3");
        Assert.Throws<InvalidDataException>(() => ModPersistentPresetEngine.PlanCombinationRuntimeRestore(
            [new(f.A, "A", [value]), new(f.B, "B", [value])], f.User));
    }

    [Fact]
    public void ModWithoutPersistentVariablesIsRecordedWithoutInventingState()
    {
        using var f = new Fixture();
        f.State("1", "2");
        File.WriteAllText(Path.Combine(f.B, "mod.ini"), "[Constants]\nglobal $temporary=1\n");
        var captured = ModPersistentPresetEngine.CaptureCombination(f.Mods, f.User);
        Assert.Empty(captured[1].Values);
        Assert.Single(ModPersistentPresetEngine.PlanCombinationRuntimeRestore(captured, f.User).Values);
    }

    [Fact]
    public void StaleCombinedPreviewDoesNotWriteAnyModState()
    {
        using var f = new Fixture();
        f.State("1", "2");
        var saved = ModPersistentPresetEngine.CaptureCombination(f.Mods, f.User);
        f.State("3", "4");
        var plan = ModPersistentPresetEngine.PlanCombinationRuntimeRestore(saved, f.User);
        string later = f.State("5", "6");
        Assert.Throws<IOException>(() => ModPersistentPresetEngine.ApplyRuntimeRestore(plan));
        Assert.Equal(later, File.ReadAllText(f.User));
        Assert.Empty(Directory.GetFiles(f.Root, "*.bak"));
    }

    [Fact]
    public void EverySourceIsRevalidatedBeforeTheSingleWrite()
    {
        using var f = new Fixture();
        f.State("1", "2");
        var saved = ModPersistentPresetEngine.CaptureCombination(f.Mods, f.User);
        string current = f.State("3", "4");
        var plan = ModPersistentPresetEngine.PlanCombinationRuntimeRestore(saved, f.User);
        File.WriteAllText(Path.Combine(f.B, "mod.ini"), "namespace=Changed\n[Constants]\nglobal persist $x=0\n");
        Assert.Throws<InvalidDataException>(() => ModPersistentPresetEngine.ApplyRuntimeRestore(plan));
        Assert.Equal(current, File.ReadAllText(f.User));
        Assert.Empty(Directory.GetFiles(f.Root, "*.bak"));
    }

    [Fact]
    public void RuntimeRollbackPreservesBackupAndRefusesLaterExternalChanges()
    {
        using var f = new Fixture();
        f.State("1", "2");
        var saved = ModPersistentPresetEngine.CaptureCombination(f.Mods, f.User);
        string current = f.State("3", "4");
        var plan = ModPersistentPresetEngine.PlanCombinationRuntimeRestore(saved, f.User);
        string backup = ModPersistentPresetEngine.ApplyRuntimeRestore(plan)!;
        ModPersistentPresetEngine.RollbackRuntimeRestore(plan, backup);
        Assert.Equal(current, File.ReadAllText(f.User));
        backup = ModPersistentPresetEngine.ApplyRuntimeRestore(plan)!;
        string later = f.State("7", "8");
        Assert.Throws<IOException>(() => ModPersistentPresetEngine.RollbackRuntimeRestore(plan, backup));
        Assert.Equal(later, File.ReadAllText(f.User));
    }

    [Fact]
    public void CopyInventoryAndPlanKeepUnknownFoldersAndRejectMissingMod()
    {
        using var f = new Fixture();
        Directory.CreateDirectory(Path.Combine(f.Target, "A"));
        Directory.CreateDirectory(Path.Combine(f.Target, "Unmanaged"));
        Assert.Equal([Path.Combine("CharacterA", "A")], ModCombinationDeploymentPolicy.Capture(f.Source, f.Target));
        var plan = ModCombinationDeploymentPolicy.Plan(f.Source, f.Target, [Path.Combine("CharacterB", "B")], false);
        Assert.Equal([f.B], plan.InstallSources);
        Assert.Equal([Path.Combine(f.Target, "A")], plan.RemoveTargets);
        Assert.Throws<DirectoryNotFoundException>(() => ModCombinationDeploymentPolicy.Plan(f.Source, f.Target, ["CharacterA\\Missing"], false));
    }

    [Theory]
    [InlineData("..\\A")]
    [InlineData("CharacterA\\..\\A")]
    [InlineData("A")]
    [InlineData("G:\\outside")]
    [InlineData("CharacterA\\A\\nested")]
    public void UnsafeModPathsFailBeforeAnyMutation(string relative)
    {
        using var f = new Fixture();
        Assert.Throws<InvalidDataException>(() => ModCombinationDeploymentPolicy.Plan(f.Source, f.Target, [relative], true));
        Assert.Empty(Directory.GetDirectories(f.Target));
    }

    [Fact]
    public void AmbiguousCopiedNamesAndDuplicateProfileEntriesAreRejected()
    {
        using var f = new Fixture();
        string duplicate = Directory.CreateDirectory(Path.Combine(f.Source, "CharacterB", "A")).FullName;
        Assert.Throws<InvalidDataException>(() => ModCombinationDeploymentPolicy.Plan(f.Source, f.Target, ["CharacterA\\A", "CharacterB\\A"], false));
        Assert.Throws<InvalidDataException>(() => ModCombinationDeploymentPolicy.Plan(f.Source, f.Target, ["CharacterA\\A", "CharacterA\\A"], false));
        Directory.CreateDirectory(Path.Combine(f.Target, "A"));
        Assert.Throws<InvalidDataException>(() => ModCombinationDeploymentPolicy.Capture(f.Source, f.Target));
        Assert.True(Directory.Exists(duplicate));
    }

    [Fact]
    public void JunctionInventoryUsesExactSourceIdentityAndPreservesForeignLinks()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var f = new Fixture();
        Directory.CreateDirectory(Path.Combine(f.Source, "CharacterB", "A"));
        string foreign = Directory.CreateDirectory(Path.Combine(f.Root, "foreign")).FullName;
        DirectoryLinkDeployment.CreateJunction(Path.Combine(f.Target, "A"), f.A);
        DirectoryLinkDeployment.CreateJunction(Path.Combine(f.Target, "B"), foreign);
        Assert.Equal(["CharacterA\\A"], ModCombinationDeploymentPolicy.Capture(f.Source, f.Target));
        var empty = ModCombinationDeploymentPolicy.Plan(f.Source, f.Target, [], true);
        Assert.Equal([Path.Combine(f.Target, "A")], empty.RemoveTargets);
        Assert.Throws<InvalidDataException>(() => ModCombinationDeploymentPolicy.Plan(f.Source, f.Target, ["CharacterB\\B"], true));
    }

    [Fact]
    public void SameCombinationStillHasStateToRestoreAndEmptyCombinationCanDisableMods()
    {
        using var f = new Fixture();
        Directory.CreateDirectory(Path.Combine(f.Target, "A"));
        var deployment = ModCombinationDeploymentPolicy.Plan(f.Source, f.Target, ["CharacterA\\A"], false);
        Assert.Empty(deployment.InstallSources);
        Assert.Empty(deployment.RemoveTargets);
        f.State("1", "2");
        var state = ModPersistentPresetEngine.CaptureCombination([f.Mods[0]], f.User);
        f.State("9", "8");
        Assert.Equal(1, ModPersistentPresetEngine.PlanCombinationRuntimeRestore(state, f.User).ChangedCount);
        Assert.Single(ModCombinationDeploymentPolicy.Plan(f.Source, f.Target, [], false).RemoveTargets);
    }

    [Fact]
    public void NestedRootsAndDisabledModCannotBeUsedAsACombination()
    {
        using var f = new Fixture();
        Assert.Throws<InvalidDataException>(() => ModCombinationDeploymentPolicy.Plan(f.Source, f.Source, [], false));
        Assert.Throws<InvalidDataException>(() => ModCombinationDeploymentPolicy.Capture(f.Source, Path.Combine(f.Source, "Mods")));
        Directory.CreateDirectory(Path.Combine(f.Source, "CharacterA", "DISABLED-old"));
        Directory.CreateDirectory(Path.Combine(f.Target, "DISABLED-old"));
        Assert.Empty(ModCombinationDeploymentPolicy.Capture(f.Source, f.Target));
        Assert.Throws<DirectoryNotFoundException>(() => ModCombinationDeploymentPolicy.Plan(f.Source, f.Target, ["CharacterA\\DISABLED-old"], false));
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "imm-combination-test-" + Guid.NewGuid().ToString("N"));
        public string Source => Path.Combine(Root, "repository");
        public string Target => Path.Combine(Root, "loader", "Mods");
        public string A => Path.Combine(Source, "CharacterA", "A");
        public string B => Path.Combine(Source, "CharacterB", "B");
        public string User => Path.Combine(Root, "d3dx_user.ini");
        public (string ModDirectory, string DeployedRelativePath)[] Mods => [(A, "A"), (B, "B")];
        public Fixture()
        {
            Directory.CreateDirectory(A); Directory.CreateDirectory(B); Directory.CreateDirectory(Target);
            foreach (var mod in Mods) File.WriteAllText(Path.Combine(mod.ModDirectory, "mod.ini"), "[Constants]\nglobal persist $x = 0\n");
        }
        public string State(string a, string b)
        {
            string text = $"; shared state\n[Constants]\n$\\Mods\\A\\mod.ini\\x = {a}\n$\\Mods\\B\\mod.ini\\x = {b}\n$\\Mods\\Unrelated\\mod.ini\\x=99 ; keep\n";
            File.WriteAllText(User, text);
            return text;
        }
        public void Dispose()
        {
            foreach (string path in Directory.GetDirectories(Target))
                if (DirectoryLinkDeployment.IsDirectoryLink(path)) DirectoryLinkDeployment.RemoveLink(path);
            Directory.Delete(Root, true);
        }
    }
}
