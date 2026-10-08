using System.Text;
using IntegratedModManager.Core;

namespace IntegratedModManager.Core.Tests;

public sealed class CombinationRestorePreviewTests
{
    [Fact]
    public void FullActionListIncludesKeptRemovedAndExternalFolders_WithoutWriting()
    {
        using var f = new Fixture();
        f.Copy("A"); f.Copy("B");
        Directory.CreateDirectory(Path.Combine(f.Target, "External"));
        var before = f.Files();
        var preview = CombinationRestorePreviewService.Prepare(f.Request() with { ModRelativePaths = ["Character\\A"], IncludeState = false });
        Assert.True(preview.CanRestore);
        Assert.Equal([CombinationModAction.Keep, CombinationModAction.Remove, CombinationModAction.PreserveExternal], preview.Mods.Select(m => m.Action));
        Assert.Null(preview.Runtime);
        Assert.Equal(before, f.Files());
    }

    [Theory]
    [InlineData("utf8")]
    [InlineData("utf8bom")]
    [InlineData("utf16le")]
    [InlineData("utf16be")]
    public void ParameterDiffUsesOneSnapshotAndPreservesEncodingCommentsAndUnrelatedKeys(string kind)
    {
        using var f = new Fixture();
        var saved = f.Request();
        string ini = Path.Combine(f.Source, "Character", "A", "mod.ini");
        File.AppendAllText(ini, "global persist $hair = 0\nglobal persist $hat = 0\n");
        saved = saved with { States = [saved.States[0] with { Values = [saved.States[0].Values[0], f.Value("A", "hair", "3"), f.Value("A", "hat", "4")] }, saved.States[1]] };
        Encoding encoding = kind switch { "utf8bom" => new UTF8Encoding(true), "utf16le" => Encoding.Unicode, "utf16be" => Encoding.BigEndianUnicode, _ => new UTF8Encoding(false) };
        string current = "; keep 中文\r\n[Constants]\r\n  $\\Mods\\A\\mod.ini\\x = 9 ; note\r\n$\\Mods\\A\\mod.ini\\hat = 4\r\n$\\Mods\\B\\mod.ini\\x = 2\r\n$\\Mods\\Other\\mod.ini\\x=99\r\n[CommandList]\r\n$\\Mods\\A\\mod.ini\\x=100\r\n";
        File.WriteAllText(f.User, current, encoding);
        byte[] before = File.ReadAllBytes(f.User);
        var preview = CombinationRestorePreviewService.Prepare(saved);
        Assert.True(preview.CanRestore);
        Assert.Equal(4, preview.Parameters.Count);
        Assert.Equal(1, preview.Runtime!.ChangedCount); Assert.Equal(1, preview.Runtime.AddedCount); Assert.Equal(2, preview.Runtime.UnchangedCount);
        var changed = Assert.Single(preview.Parameters, p => p.Change.Kind == ModPersistentChangeKind.Changed);
        Assert.Equal("9", changed.Change.PreviousValue); Assert.Equal("1", changed.Change.Saved.Value);
        Assert.Equal("Character\\A", changed.ModRelativePath);
        Assert.Null(Assert.Single(preview.Parameters, p => p.Change.Kind == ModPersistentChangeKind.Added).Change.PreviousValue);
        Assert.Equal(before, File.ReadAllBytes(f.User));
        Assert.Empty(Directory.GetFiles(f.Root, "*.bak", SearchOption.AllDirectories));
        string backup = Assert.IsType<string>(ModPersistentPresetEngine.ApplyRuntimeRestore(preview.Runtime));
        Assert.Equal(before, File.ReadAllBytes(backup));
        string updated = File.ReadAllText(f.User);
        Assert.Contains("= 1 ; note", updated); Assert.Contains("\\hair = 3", updated);
        Assert.Contains("\\Other\\mod.ini\\x=99", updated); Assert.Contains("[CommandList]\r\n$\\Mods\\A\\mod.ini\\x=100", updated);
        Assert.Equal(encoding.GetPreamble(), File.ReadAllBytes(f.User).Take(encoding.GetPreamble().Length));
        Assert.Contains("global persist $x = 0", File.ReadAllText(ini));
        ModPersistentPresetEngine.RollbackRuntimeRestore(preview.Runtime, backup);
        Assert.Equal(before, File.ReadAllBytes(f.User));
    }

    [Fact]
    public void ListsMoreThanTwentyModsAndParametersWithoutTruncation()
    {
        using var f = new Fixture();
        var paths = new List<string>(); var states = new List<CombinationPresetState>();
        for (int i = 0; i < 100; i++)
        {
            string name = "M" + i;
            f.Add("Character", name);
            paths.Add("Character\\" + name); states.Add(new(paths[^1], [f.Value(name, "x", "1")]));
        }
        var preview = CombinationRestorePreviewService.Prepare(f.Request() with { ModRelativePaths = paths, States = states });
        Assert.True(preview.CanRestore); Assert.Equal(100, preview.Mods.Count); Assert.Equal(100, preview.Parameters.Count);
        Assert.Equal(100, preview.Runtime!.AddedCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyCombinationCanRemoveRecognizedDeploymentsWithoutStateFile(bool state)
    {
        using var f = new Fixture(); f.Copy("A"); File.Delete(f.User);
        var preview = CombinationRestorePreviewService.Prepare(f.Request() with { ModRelativePaths = [], States = [], IncludeState = state });
        Assert.True(preview.CanRestore); Assert.Null(preview.Runtime);
        Assert.Equal(CombinationModAction.Remove, Assert.Single(preview.Mods).Action);
    }

    [Fact]
    public void MultipleMissingModsAreAggregatedAndNeverPartiallyRestored()
    {
        using var f = new Fixture();
        Directory.Delete(Path.Combine(f.Source, "Character", "A"), true);
        Directory.Delete(Path.Combine(f.Source, "Character", "B"), true);
        var preview = CombinationRestorePreviewService.Prepare(f.Request());
        Assert.False(preview.CanRestore); Assert.Null(preview.Deployment);
        Assert.Equal(2, preview.Issues.Count(i => i.Code == CombinationPreviewIssueCode.MissingMod));
        Assert.All(preview.Mods, m => Assert.Equal(CombinationModAction.Unavailable, m.Action));
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData(" ")] [InlineData("A")] [InlineData("A/B/C")]
    [InlineData("../A")] [InlineData("A/..")] [InlineData("C:/A")]
    [InlineData("/A/B")] [InlineData("A/B.")] [InlineData("A/B ")] [InlineData("A/B:bad")]
    public void UnsafePathsProduceIssuesInsteadOfExecutablePlans(string? path)
    {
        using var f = new Fixture();
        var preview = CombinationRestorePreviewService.Prepare(f.Request() with { ModRelativePaths = [path!], IncludeState = false });
        Assert.False(preview.CanRestore); Assert.Contains(preview.Issues, i => i.Code == CombinationPreviewIssueCode.UnsafeModPath);
    }

    [Theory]
    [InlineData("DISABLED-character", "A")]
    [InlineData("Character", "DISABLED-A")]
    public void DisabledFoldersBlockRestore(string category, string name)
    {
        using var f = new Fixture(); f.Add(category, name);
        var preview = CombinationRestorePreviewService.Prepare(f.Request() with { ModRelativePaths = [category + "\\" + name], IncludeState = false });
        Assert.Contains(preview.Issues, i => i.Code == CombinationPreviewIssueCode.DisabledMod);
    }

    [Fact]
    public void DuplicatePathsAndTargetNamesAreReported()
    {
        using var f = new Fixture(); f.Add("Other", "A");
        var duplicate = CombinationRestorePreviewService.Prepare(f.Request() with { ModRelativePaths = ["Character\\A", "character/A"], IncludeState = false });
        Assert.Contains(duplicate.Issues, i => i.Code == CombinationPreviewIssueCode.DuplicateMod);
        var ambiguous = CombinationRestorePreviewService.Prepare(f.Request() with { ModRelativePaths = ["Character\\A", "Other\\A"], IncludeState = false });
        Assert.Contains(ambiguous.Issues, i => i.Code == CombinationPreviewIssueCode.AmbiguousTargetName);
        Assert.False(ambiguous.CanRestore);
    }

    [Theory]
    [InlineData("missingSource", CombinationPreviewIssueCode.SourceUnavailable)]
    [InlineData("missingTarget", CombinationPreviewIssueCode.TargetUnavailable)]
    [InlineData("same", CombinationPreviewIssueCode.UnsafeRoots)]
    [InlineData("nested", CombinationPreviewIssueCode.UnsafeRoots)]
    [InlineData("parent", CombinationPreviewIssueCode.UnsafeRoots)]
    [InlineData("invalid", CombinationPreviewIssueCode.UnsafeRoots)]
    public void UnsafeRootsBlockRestore(string mode, CombinationPreviewIssueCode expected)
    {
        using var f = new Fixture(); var request = f.Request();
        request = mode switch
        {
            "missingSource" => request with { SourceRoot = Path.Combine(f.Root, "missing") },
            "missingTarget" => request with { TargetRoot = Path.Combine(f.Root, "missing") },
            "same" => request with { TargetRoot = f.Source },
            "nested" => request with { TargetRoot = Path.Combine(f.Source, "Character") },
            "parent" => request with { SourceRoot = f.Root },
            _ => request with { SourceRoot = "\0" }
        };
        var preview = CombinationRestorePreviewService.Prepare(request);
        Assert.False(preview.CanRestore); Assert.Contains(preview.Issues, i => i.Code == expected);
    }

    [Theory]
    [InlineData("mod")]
    [InlineData("category")]
    [InlineData("sourceRoot")]
    [InlineData("targetRoot")]
    public void LinkedSourceComponentsAndRootsAreRejected(string mode)
    {
        using var f = new Fixture(); string foreign = Directory.CreateDirectory(Path.Combine(f.Root, "foreign")).FullName;
        string path = mode switch { "mod" => Path.Combine(f.Source, "Character", "A"), "category" => Path.Combine(f.Source, "Character"), "sourceRoot" => f.Source, _ => f.Target };
        Directory.Delete(path, true); DirectoryLinkDeployment.CreateJunction(path, foreign);
        f.Links.Add(path);
        var preview = CombinationRestorePreviewService.Prepare(f.Request());
        Assert.False(preview.CanRestore);
        Assert.Contains(preview.Issues, i => i.Code == (mode.EndsWith("Root") ? CombinationPreviewIssueCode.UnsafeRoots : CombinationPreviewIssueCode.LinkedSource));
        Assert.True(Directory.Exists(foreign));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("foreign")]
    [InlineData("nullPaths")]
    [InlineData("nullStates")]
    [InlineData("nullState")]
    public void InconsistentSnapshotsBlockWholeCombination(string mode)
    {
        using var f = new Fixture(); var request = f.Request();
        request = mode switch
        {
            "missing" => request with { States = [request.States[0]] },
            "duplicate" => request with { States = [request.States[0], request.States[0]] },
            "foreign" => request with { States = [request.States[0], request.States[1] with { ModRelativePath = "Other\\Z" }] },
            "nullPaths" => request with { ModRelativePaths = null! },
            "nullStates" => request with { States = null! },
            _ => request with { States = [null!, request.States[1]] }
        };
        var preview = CombinationRestorePreviewService.Prepare(request);
        Assert.False(preview.CanRestore); Assert.Contains(preview.Issues, i => i.Code == CombinationPreviewIssueCode.SnapshotMismatch);
    }

    [Fact]
    public void SnapshotPathsNormalizeCaseAndSlashes()
    {
        using var f = new Fixture(); var request = f.Request();
        request = request with { States = request.States.Select(s => s with { ModRelativePath = s.ModRelativePath.ToLowerInvariant().Replace('\\', '/') }).ToArray() };
        Assert.True(CombinationRestorePreviewService.Prepare(request).CanRestore);
    }

    [Theory]
    [InlineData("declaration")]
    [InlineData("namespace")]
    [InlineData("missingIni")]
    [InlineData("badValue")]
    [InlineData("nullValue")]
    [InlineData("nullValues")]
    public void InvalidParametersAreReportedPerModWithoutWriting(string mode)
    {
        using var f = new Fixture(); var request = f.Request();
        string ini = Path.Combine(f.Source, "Character", "A", "mod.ini");
        if (mode == "declaration") File.WriteAllText(ini, "[Constants]\nglobal $x=0\n");
        if (mode == "namespace") File.WriteAllText(ini, "namespace=Changed\n" + File.ReadAllText(ini));
        if (mode == "missingIni") File.Delete(ini);
        if (mode == "badValue") request = request with { States = [request.States[0] with { Values = [f.Value("A", "x", "NaN")] }, request.States[1]] };
        if (mode == "nullValue") request = request with { States = [request.States[0] with { Values = [null!] }, request.States[1]] };
        if (mode == "nullValues") request = request with { States = [request.States[0] with { Values = null! }, request.States[1]] };
        byte[] before = File.ReadAllBytes(f.User);
        var preview = CombinationRestorePreviewService.Prepare(request);
        Assert.False(preview.CanRestore); Assert.Contains(preview.Issues, i => i.Code == CombinationPreviewIssueCode.ParameterMismatch && i.Subject == "Character\\A");
        Assert.Equal(before, File.ReadAllBytes(f.User));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("invalid")]
    public void UnsafeLoaderStateIsBlocked(string mode)
    {
        using var f = new Fixture(); var request = f.Request();
        if (mode == "missing") File.Delete(f.User);
        if (mode == "duplicate") File.AppendAllText(f.User, "$\\Mods\\A\\mod.ini\\x=9\n");
        if (mode == "invalid") File.WriteAllBytes(f.User, [0xff, 0xff, 0xff]);
        var preview = CombinationRestorePreviewService.Prepare(request);
        Assert.False(preview.CanRestore); Assert.Null(preview.Runtime);
        Assert.Contains(preview.Issues, i => i.Code == CombinationPreviewIssueCode.LoaderStateInvalid);
    }

    [Fact]
    public void DeployedCopyDeclarationsAreCheckedBeforeTransaction()
    {
        using var f = new Fixture(); f.Copy("A");
        File.WriteAllText(Path.Combine(f.Target, "A", "mod.ini"), "namespace=Other\n[Constants]\nglobal persist $x=0\n");
        var preview = CombinationRestorePreviewService.Prepare(f.Request());
        Assert.False(preview.CanRestore); Assert.Contains(preview.Issues, i => i.Code == CombinationPreviewIssueCode.ParameterMismatch);
    }

    [Theory]
    [InlineData("file")]
    [InlineData("foreignLink")]
    [InlineData("ambiguousCopy")]
    public void StrictDeploymentPlannerRemainsFinalAuthority(string mode)
    {
        using var f = new Fixture();
        if (mode == "file") File.WriteAllText(Path.Combine(f.Target, "A"), "keep");
        if (mode == "foreignLink")
        {
            string foreign = Directory.CreateDirectory(Path.Combine(f.Root, "foreign")).FullName;
            string link = Path.Combine(f.Target, "A"); DirectoryLinkDeployment.CreateJunction(link, foreign); f.Links.Add(link);
        }
        if (mode == "ambiguousCopy") { f.Add("Other", "A"); f.Copy("A"); }
        var preview = CombinationRestorePreviewService.Prepare(f.Request());
        Assert.False(preview.CanRestore); Assert.Null(preview.Deployment);
        Assert.Contains(preview.Issues, i => i.Code == CombinationPreviewIssueCode.DeploymentBlocked);
    }

    [Fact]
    public void ValidRepositoryLinksKeepIdentityAndPreserveExternalLinks()
    {
        using var f = new Fixture();
        string own = Path.Combine(f.Target, "A"), external = Path.Combine(f.Target, "External");
        string foreign = Directory.CreateDirectory(Path.Combine(f.Root, "foreign")).FullName;
        DirectoryLinkDeployment.CreateJunction(own, Path.Combine(f.Source, "Character", "A")); f.Links.Add(own);
        DirectoryLinkDeployment.CreateJunction(external, foreign); f.Links.Add(external);
        var preview = CombinationRestorePreviewService.Prepare(f.Request() with { UseLinks = true });
        Assert.True(preview.CanRestore); Assert.Equal(CombinationModAction.Keep, preview.Mods[0].Action);
        Assert.Contains(preview.Mods, m => m.Path == external && m.Action == CombinationModAction.PreserveExternal);
    }

    [Theory]
    [InlineData("deployment")]
    [InlineData("state")]
    [InlineData("unrelatedState")]
    [InlineData("declarations")]
    [InlineData("missingSource")]
    public void ConfirmationCannotApproveAStalePlan(string mode)
    {
        using var f = new Fixture(); var request = f.Request();
        var original = CombinationRestorePreviewService.Prepare(request);
        Assert.True(CombinationRestorePreviewService.IsStillApproved(original, CombinationRestorePreviewService.Prepare(request)));
        if (mode == "deployment") f.Copy("A");
        if (mode == "state") File.WriteAllText(f.User, "[Constants]\n$\\Mods\\A\\mod.ini\\x=8\n");
        if (mode == "unrelatedState") File.AppendAllText(f.User, "; external edit\n");
        if (mode == "declarations") File.WriteAllText(Path.Combine(f.Source, "Character", "A", "mod.ini"), "namespace=Changed\n[Constants]\nglobal persist $x=0\n");
        if (mode == "missingSource") Directory.Delete(Path.Combine(f.Source, "Character", "A"), true);
        Assert.False(CombinationRestorePreviewService.IsStillApproved(original, CombinationRestorePreviewService.Prepare(request)));
    }

    [Fact]
    public void ApprovalRequiresBothValidPlansAndSameRuntimePresence()
    {
        using var f = new Fixture();
        var valid = CombinationRestorePreviewService.Prepare(f.Request());
        var noState = CombinationRestorePreviewService.Prepare(f.Request() with { IncludeState = false });
        Assert.True(CombinationRestorePreviewService.IsStillApproved(noState, noState));
        Assert.False(CombinationRestorePreviewService.IsStillApproved(valid, noState));
        var blocked = CombinationRestorePreviewService.Prepare(f.Request() with { SourceRoot = "\0" });
        Assert.False(CombinationRestorePreviewService.IsStillApproved(blocked, valid));
        Assert.False(CombinationRestorePreviewService.IsStillApproved(valid, blocked));
        Assert.False(CombinationRestorePreviewService.IsStillApproved(valid, valid with { Runtime = valid.Runtime! with { Edit = valid.Runtime.Edit with { Path = f.User + "other" } } }));
        Assert.False(CombinationRestorePreviewService.IsStillApproved(valid, valid with { Runtime = valid.Runtime! with { Edit = valid.Runtime.Edit with { UpdatedBytes = [0] } } }));
    }

    [Fact]
    public void MissingModDoesNotMislabelUncheckedValidModsAsAlreadyEnabled()
    {
        using var f = new Fixture(); Directory.Delete(Path.Combine(f.Source, "Character", "B"), true);
        var preview = CombinationRestorePreviewService.Prepare(f.Request());
        Assert.Equal(CombinationModAction.Pending, preview.Mods[0].Action);
        Assert.Equal(CombinationModAction.Unavailable, preview.Mods[1].Action);
    }

    [Fact]
    public void ExternalFolderInventoryChangesRequireANewPreview()
    {
        using var f = new Fixture(); var request = f.Request();
        var preview = CombinationRestorePreviewService.Prepare(request);
        Directory.CreateDirectory(Path.Combine(f.Target, "External"));
        Assert.False(CombinationRestorePreviewService.IsStillApproved(preview, CombinationRestorePreviewService.Prepare(request)));
        Assert.Empty(Directory.GetFiles(f.Root, "*.bak", SearchOption.AllDirectories));
    }

    [Fact]
    public void SharedRuntimeKeysAcrossModsAreBlockedRatherThanAssignedToAnArbitraryOwner()
    {
        using var f = new Fixture();
        foreach (string name in new[] { "A", "B" }) File.WriteAllText(Path.Combine(f.Source, "Character", name, "mod.ini"), "namespace=Shared\n[Constants]\nglobal persist $x=0\n");
        File.WriteAllText(f.User, "[Constants]\n$\\Shared\\x=1\n");
        var request = f.Request() with { States = [new("Character\\A", [new("mod.ini", "$x", "$\\Shared\\x", "1")]), new("Character\\B", [new("mod.ini", "$x", "$\\Shared\\x", "2")])] };
        var preview = CombinationRestorePreviewService.Prepare(request);
        Assert.False(preview.CanRestore); Assert.Contains(preview.Issues, i => i.Code == CombinationPreviewIssueCode.LoaderStateInvalid);
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "imm-preview-test-" + Guid.NewGuid().ToString("N"));
        public string Source => Path.Combine(Root, "repository");
        public string Target => Path.Combine(Root, "loader", "Mods");
        public string User => Path.Combine(Root, "d3dx_user.ini");
        public List<string> Links { get; } = [];
        public Fixture()
        {
            Add("Character", "A"); Add("Character", "B"); Directory.CreateDirectory(Target);
            File.WriteAllText(User, "[Constants]\n$\\Mods\\A\\mod.ini\\x = 1\n$\\Mods\\B\\mod.ini\\x = 2\n$\\Mods\\Other\\mod.ini\\x=99\n");
        }
        public void Add(string category, string name)
        {
            string mod = Directory.CreateDirectory(Path.Combine(Source, category, name)).FullName;
            File.WriteAllText(Path.Combine(mod, "mod.ini"), "[Constants]\nglobal persist $x = 0\n");
        }
        public void Copy(string name)
        {
            string target = Directory.CreateDirectory(Path.Combine(Target, name)).FullName;
            File.Copy(Path.Combine(Source, "Character", name, "mod.ini"), Path.Combine(target, "mod.ini"));
        }
        public ModPersistentValue Value(string name, string variable, string value) => new("mod.ini", "$" + variable, "$\\Mods\\" + name + "\\mod.ini\\" + variable, value);
        public CombinationRestoreRequest Request() => new(Source, Target, ["Character\\A", "Character\\B"], false, true,
            [new("Character\\A", [Value("A", "x", "1")]), new("Character\\B", [Value("B", "x", "2")])], User);
        public string[] Files() => Directory.GetFiles(Root, "*", SearchOption.AllDirectories).Order().Select(p => p + ":" + Convert.ToHexString(File.ReadAllBytes(p))).ToArray();
        public void Dispose()
        {
            foreach (string link in Links) DirectoryLinkDeployment.RemoveLink(link);
            Directory.Delete(Root, true);
        }
    }
}
