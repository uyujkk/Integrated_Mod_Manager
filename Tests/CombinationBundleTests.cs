using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IntegratedModManager.Core;

namespace IntegratedModManager.Core.Tests;

public sealed class CombinationBundleTests
{
    [Fact]
    public void ExportInstallRestore_RoundTripPreservesOtherStateAndModDefaults()
    {
        using var f = new Fixture();
        f.Export();
        using (var zip = ZipFile.OpenRead(f.Zip))
        {
            Assert.Equal(3, zip.Entries.Count);
            Assert.DoesNotContain(zip.Entries, entry => entry.FullName.Contains("d3dx_user", StringComparison.OrdinalIgnoreCase));
            using var reader = new StreamReader(zip.GetEntry("preset.json")!.Open());
            Assert.DoesNotContain(f.Root, reader.ReadToEnd());
        }
        using var import = f.Prepare();
        Assert.True(import.CanInstall);
        Assert.All(import.Preview, item => Assert.Equal(CombinationBundleDisposition.Install, item.Disposition));
        Assert.False(Directory.Exists(Path.Combine(f.Destination, "角色")));
        import.InstallFiles();
        CombinationBundleManifest manifest = import.Manifest;
        string profileConfig = Path.Combine(f.Root, "imported-profile.json");
        File.WriteAllText(profileConfig, JsonSerializer.Serialize(manifest));
        import.Complete();
        Assert.True(import.IsCommitted);
        var deployment = ModCombinationDeploymentPolicy.Plan(f.Destination, f.Target,
            manifest.Mods.Select(mod => mod.RelativePath.Replace('/', '\\')).ToArray(), false);
        foreach (string source in deployment.InstallSources)
        {
            string target = Path.Combine(f.Target, Path.GetFileName(source));
            Directory.CreateDirectory(target);
            File.Copy(Path.Combine(source, "mod.ini"), Path.Combine(target, "mod.ini"));
        }
        Assert.Equal(2, ModCombinationDeploymentPolicy.Capture(f.Destination, f.Target).Count);
        string before = "[Constants]\n$\\Mods\\A\\mod.ini\\x = 9\n$\\Mods\\B\\mod.ini\\x = 8\n$\\Other\\y = 7\n";
        File.WriteAllText(f.User, before);
        var runtime = ModPersistentPresetEngine.PlanCombinationRuntimeRestore(manifest.Mods.Select(mod =>
            new ModPersistentSourceState(Path.Combine(f.Destination, mod.RelativePath), Path.GetFileName(mod.RelativePath), mod.Values)).ToArray(), f.User);
        string backup = ModPersistentPresetEngine.ApplyRuntimeRestore(runtime)!;
        Assert.Equal(before, File.ReadAllText(backup));
        Assert.Contains("\\x = 1", File.ReadAllText(f.User));
        Assert.Contains("\\x = 2", File.ReadAllText(f.User));
        Assert.Contains("$\\Other\\y = 7", File.ReadAllText(f.User));
        Assert.Equal(Fixture.Ini, File.ReadAllText(Path.Combine(f.Destination, "角色", "A", "mod.ini")));
        ModPersistentPresetEngine.RollbackRuntimeRestore(runtime, backup);
        Assert.Equal(before, File.ReadAllText(f.User));
    }

    [Fact]
    public void SameFilesAreReused_ExistingFilesNeverOverwritten()
    {
        using var f = new Fixture(); f.Export();
        f.AddExisting("A", Fixture.Ini);
        using var import = f.Prepare();
        Assert.Equal(CombinationBundleDisposition.Reuse, import.Preview[0].Disposition);
        import.InstallFiles(); import.Complete(); import.Dispose();
        Assert.True(File.Exists(Path.Combine(f.Destination, "角色", "A", "mod.ini")));
        Assert.Empty(Directory.GetDirectories(f.Staging));
        Assert.Empty(import.CleanupWarnings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReexportImportedPreset_ToAnotherRepositoryPreservesSavedStateAndReusesFiles(bool state)
    {
        using var f = new Fixture();
        f.Export(state: state);
        CombinationBundleManifest saved;
        using (var first = f.Prepare())
        {
            first.InstallFiles();
            // Persist/read the snapshot before re-export, rather than recapturing live defaults.
            string profile = JsonSerializer.Serialize(first.Manifest);
            saved = JsonSerializer.Deserialize<CombinationBundleManifest>(profile)!;
            first.Complete();
        }
        const string unrelated = "[Constants]\n$\\Other\\keep = 99\n";
        File.WriteAllText(f.User, unrelated);
        byte[] userBefore = File.ReadAllBytes(f.User);
        string exported = Path.Combine(f.Root, "再次导出的组合包.zip");
        CombinationBundleService.Export(f.Destination, exported, saved.Name, saved.Game,
            saved.IncludesPersistentState, saved.Mods.Select(mod =>
                new CombinationPresetState(mod.RelativePath, mod.Values)).ToArray());
        using (var zip = ZipFile.OpenRead(exported))
        {
            Assert.DoesNotContain(zip.Entries, entry => entry.FullName.Contains("d3dx_user", StringComparison.OrdinalIgnoreCase));
            using var reader = new StreamReader(zip.GetEntry("preset.json")!.Open());
            Assert.DoesNotContain(f.Root, reader.ReadToEnd());
        }
        string otherRepository = Path.Combine(f.Root, "另一仓库");
        string otherStaging = Path.Combine(f.Root, "另一暂存目录");
        Directory.CreateDirectory(otherRepository);
        Directory.CreateDirectory(otherStaging);
        using (var second = CombinationBundleService.PrepareImport(exported, otherRepository, otherStaging))
        {
            Assert.True(second.CanInstall);
            Assert.Equal(saved.IncludesPersistentState, second.Manifest.IncludesPersistentState);
            Assert.Equal(state ? 2 : 0, second.Manifest.Mods.Sum(mod => mod.Values.Count));
            Assert.Equal(JsonSerializer.Serialize(saved.Mods), JsonSerializer.Serialize(second.Manifest.Mods));
            Assert.All(second.Preview, item => Assert.Equal(CombinationBundleDisposition.Install, item.Disposition));
            second.InstallFiles();
            second.Complete();
        }
        using (var repeat = CombinationBundleService.PrepareImport(exported, otherRepository, otherStaging))
        {
            Assert.True(repeat.CanInstall);
            Assert.All(repeat.Preview, item => Assert.Equal(CombinationBundleDisposition.Reuse, item.Disposition));
            repeat.InstallFiles();
            repeat.Complete();
        }
        foreach (string mod in new[] { "A", "B" })
        {
            Assert.Equal(Fixture.Ini, File.ReadAllText(Path.Combine(f.Destination, "角色", mod, "mod.ini")));
            Assert.Equal(Fixture.Ini, File.ReadAllText(Path.Combine(otherRepository, "角色", mod, "mod.ini")));
        }
        Assert.Equal(userBefore, File.ReadAllBytes(f.User));
        Assert.Empty(Directory.GetFileSystemEntries(f.Target));
        Assert.Empty(Directory.GetFileSystemEntries(f.Staging));
        Assert.Empty(Directory.GetFileSystemEntries(otherStaging));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingChangedOrExtraFiles_BlockWholeInstall(bool extra)
    {
        using var f = new Fixture(); f.Export();
        f.AddExisting("A", extra ? Fixture.Ini : "different");
        if (extra) File.WriteAllText(Path.Combine(f.Destination, "角色", "A", "other.txt"), "keep");
        using var import = f.Prepare();
        Assert.False(import.CanInstall);
        Assert.Throws<InvalidDataException>(() => import.InstallFiles());
        Assert.False(Directory.Exists(Path.Combine(f.Destination, "角色", "B")));
        Assert.Equal(extra ? Fixture.Ini : "different", File.ReadAllText(Path.Combine(f.Destination, "角色", "A", "mod.ini")));
    }

    [Fact]
    public void FailureBeforeConfigCommit_RollsBackNewFoldersAndPreservesReusedMod()
    {
        using var f = new Fixture(); f.Export(); f.AddExisting("A", Fixture.Ini);
        var import = f.Prepare(); import.InstallFiles();
        import.Dispose(); import.Dispose();
        Assert.True(Directory.Exists(Path.Combine(f.Destination, "角色", "A")));
        Assert.False(Directory.Exists(Path.Combine(f.Destination, "角色", "B")));
        Assert.Empty(import.CleanupWarnings);
        Assert.Throws<InvalidOperationException>(() => import.InstallFiles());
        Assert.Throws<InvalidOperationException>(() => import.Complete());
    }

    [Fact]
    public void RollbackRefusesToDeleteFolderEditedAfterInstallation()
    {
        using var f = new Fixture(); f.Export();
        var import = f.Prepare(); import.InstallFiles();
        string edited = Path.Combine(f.Destination, "角色", "A", "mod.ini");
        File.WriteAllText(edited, "external edit");
        import.Dispose();
        Assert.Equal("external edit", File.ReadAllText(edited));
        Assert.Single(import.CleanupWarnings);
        Assert.False(Directory.Exists(Path.Combine(f.Destination, "角色", "B")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChangedRepositoryAfterPreview_IsRejectedBeforeAnyInstall(bool existing)
    {
        using var f = new Fixture(); f.Export();
        if (existing) f.AddExisting("A", Fixture.Ini);
        using var import = f.Prepare();
        f.AddExisting("A", "changed after preview");
        Assert.Throws<IOException>(() => import.InstallFiles());
        Assert.False(Directory.Exists(Path.Combine(f.Destination, "角色", "B")));
    }

    [Fact]
    public void ChangedStagingAfterPreview_IsRejectedBeforeInstall()
    {
        using var f = new Fixture(); f.Export(); using var import = f.Prepare();
        string stage = Assert.Single(Directory.GetDirectories(f.Staging));
        File.WriteAllText(Path.Combine(stage, "000000", "mod.ini"), "changed");
        Assert.Throws<IOException>(() => import.InstallFiles());
        Assert.Empty(Directory.GetDirectories(f.Destination));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("/absolute")]
    [InlineData("C:/absolute")]
    [InlineData("角色/..")]
    [InlineData("角色/A/third")]
    [InlineData("DISABLED角色/A")]
    [InlineData("角色/DISABLED_A")]
    [InlineData("角色/CON.txt")]
    [InlineData("角色/A.")]
    [InlineData("角色/A ")]
    [InlineData("角色/A:ads")]
    [InlineData("角色/COM¹")]
    [InlineData("角色/A?file")]
    public void UnsafeManifestModPath_RejectedWithoutRepositoryWrites(string relative)
    {
        using var f = new Fixture(); f.Export();
        f.ChangeManifest(json => json["mods"]![0]!["relativePath"] = relative);
        Assert.Throws<InvalidDataException>(() => f.Prepare());
        Assert.Empty(Directory.GetDirectories(f.Destination));
        Assert.Empty(Directory.GetDirectories(f.Staging));
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("mods/000000/../escape.txt")]
    [InlineData("mods\\000000\\mod.ini")]
    [InlineData("mods/000000/dir/")]
    [InlineData("mods/000000/aux.txt")]
    public void UnsafeZipEntry_RejectedBeforeExtraction(string name)
    {
        using var f = new Fixture(); f.Export();
        f.AddEntry(name, "malicious");
        Assert.Throws<InvalidDataException>(() => f.Prepare());
        Assert.Empty(Directory.GetDirectories(f.Staging));
    }

    [Theory]
    [InlineData(0xA000)]
    [InlineData(0x4000)]
    [InlineData(0x6000)]
    public void UnixLinkDirectoryOrDeviceEntry_Rejected(int type)
    {
        using var f = new Fixture(); f.Export();
        using (var zip = ZipFile.Open(f.Zip, ZipArchiveMode.Update)) zip.GetEntry("mods/000000/mod.ini")!.ExternalAttributes = type << 16;
        Assert.Throws<InvalidDataException>(() => f.Prepare());
    }

    [Fact]
    public void DuplicateCaseAliasEntry_Rejected()
    {
        using var f = new Fixture(); f.Export(); f.AddEntry("MODS/000000/MOD.INI", Fixture.Ini);
        Assert.Throws<InvalidDataException>(() => f.Prepare());
    }

    [Fact]
    public void UnlistedEntry_Rejected()
    {
        using var f = new Fixture(); f.Export(); f.AddEntry("mods/000000/extra.txt", "extra");
        Assert.Throws<InvalidDataException>(() => f.Prepare());
    }

    [Fact]
    public void MissingPayload_Rejected()
    {
        using var f = new Fixture(); f.Export();
        using (var zip = ZipFile.Open(f.Zip, ZipArchiveMode.Update)) zip.GetEntry("mods/000000/mod.ini")!.Delete();
        Assert.Throws<InvalidDataException>(() => f.Prepare());
    }

    [Fact]
    public void WrongHash_IsRejectedAndStagingCleaned()
    {
        using var f = new Fixture(); f.Export();
        f.ChangeManifest(json => json["mods"]![0]!["files"]![0]!["sha256"] = new string('0', 64));
        Assert.Throws<InvalidDataException>(() => f.Prepare());
        Assert.Empty(Directory.GetDirectories(f.Staging));
    }

    [Fact]
    public void WrongDeclaredLength_IsRejected()
    {
        using var f = new Fixture(); f.Export();
        f.ChangeManifest(json => json["mods"]![0]!["files"]![0]!["length"] = 500);
        Assert.Throws<InvalidDataException>(() => f.Prepare());
    }

    [Fact]
    public void ForeignRuntimeKey_IsRejectedWithoutLoaderWrite()
    {
        using var f = new Fixture(); f.Export();
        File.WriteAllText(f.User, "untouched");
        f.ChangeManifest(json => json["mods"]![0]!["values"]![0]!["runtimeKey"] = "$\\Other\\x");
        Assert.Throws<InvalidDataException>(() => f.Prepare());
        Assert.Equal("untouched", File.ReadAllText(f.User));
    }

    [Theory]
    [InlineData("version", "2")]
    [InlineData("format", "\"other\"")]
    [InlineData("name", "\"\"")]
    [InlineData("mods", "null")]
    [InlineData("game", "null")]
    [InlineData("includesPersistentState", "false")]
    public void InvalidManifestShape_IsRejected(string key, string value)
    {
        using var f = new Fixture(); f.Export();
        f.ChangeManifest(json => json[key] = JsonNode.Parse(value));
        Assert.Throws<InvalidDataException>(() => f.Prepare());
    }

    [Fact]
    public void MissingRequiredProperty_IsRejected()
    {
        using var f = new Fixture(); f.Export();
        f.ChangeManifest(json => json.AsObject().Remove("includesPersistentState"));
        Assert.Throws<JsonException>(() => f.Prepare());
    }

    [Fact]
    public void DuplicateJsonProperty_IsRejected()
    {
        using var f = new Fixture(); f.Export();
        f.RewriteManifest(text => text.Replace("\"version\": 1", "\"version\": 1, \"version\": 1"));
        Assert.Throws<InvalidDataException>(() => f.Prepare());
    }

    [Fact]
    public void DuplicateTargetNames_AreRejected()
    {
        using var f = new Fixture(); f.Export();
        f.ChangeManifest(json => json["mods"]![1]!["relativePath"] = "其他/A");
        Assert.Throws<InvalidDataException>(() => f.Prepare());
    }

    [Fact]
    public void DuplicateRuntimeKeys_AreRejected()
    {
        using var f = new Fixture(); f.Export();
        f.ChangeManifest(json => json["mods"]![1]!["values"]![0]!["runtimeKey"] = "$\\Mods\\A\\mod.ini\\x");
        Assert.Throws<InvalidDataException>(() => f.Prepare());
    }

    [Fact]
    public void OutputAlreadyExists_NotOverwritten_AndNoPartialRetained()
    {
        using var f = new Fixture(); File.WriteAllText(f.Zip, "keep");
        Assert.Throws<IOException>(() => f.Export());
        Assert.Equal("keep", File.ReadAllText(f.Zip));
        Assert.Empty(Directory.GetFiles(f.Root, "*.partial"));
    }

    [Fact]
    public void ExportIntoRepository_IsRejected()
    {
        using var f = new Fixture();
        Assert.Throws<InvalidDataException>(() => f.Export(Path.Combine(f.Source, "bundle.zip")));
    }

    [Fact]
    public void EmptyCombination_CanBePackagedAndCommitted()
    {
        using var f = new Fixture();
        CombinationBundleService.Export(f.Source, f.Zip, "empty", "", false, []);
        using var import = f.Prepare();
        Assert.Empty(import.Manifest.Mods); import.InstallFiles(); import.Complete();
        Assert.Empty(Directory.GetDirectories(f.Destination));
    }

    [Fact]
    public void EmptyModDirectory_AndNestedUnicodeFiles_AreSupported()
    {
        using var f = new Fixture();
        File.Delete(Path.Combine(f.Source, "角色", "A", "mod.ini"));
        Directory.CreateDirectory(Path.Combine(f.Source, "角色", "B", "素材"));
        File.WriteAllText(Path.Combine(f.Source, "角色", "B", "素材", "清晰 图.txt"), "data");
        f.Export(state: false);
        using var import = f.Prepare(); import.InstallFiles(); import.Complete();
        Assert.True(Directory.Exists(Path.Combine(f.Destination, "角色", "A")));
        Assert.Equal("data", File.ReadAllText(Path.Combine(f.Destination, "角色", "B", "素材", "清晰 图.txt")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Precancellation_LeavesNoArchiveOrRepositoryChanges(int stage)
    {
        using var f = new Fixture();
        var token = new CancellationToken(true);
        if (stage == 0) Assert.Throws<OperationCanceledException>(() => f.Export(token: token));
        else
        {
            f.Export();
            if (stage == 1) Assert.Throws<OperationCanceledException>(() => CombinationBundleService.PrepareImport(f.Zip, f.Destination, f.Staging, token));
            else { using var import = f.Prepare(); Assert.Throws<OperationCanceledException>(() => import.InstallFiles(token)); }
        }
        Assert.Empty(Directory.GetDirectories(f.Destination));
        Assert.Empty(Directory.GetDirectories(f.Staging));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ExportLimits_CleanOwnedPartial(int kind)
    {
        using var f = new Fixture();
        var limits = kind switch
        {
            0 => new CombinationBundleLimits(MaxMods: 1),
            1 => new CombinationBundleLimits(MaxFiles: 1),
            2 => new CombinationBundleLimits(MaxFileBytes: 1),
            3 => new CombinationBundleLimits(MaxManifestBytes: 1),
            _ => new CombinationBundleLimits(MaxArchiveBytes: 1)
        };
        Assert.Throws<InvalidDataException>(() => f.Export(limits: limits));
        Assert.False(File.Exists(f.Zip));
        Assert.Empty(Directory.GetFiles(f.Root, "*.partial"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ImportLimits_RejectBeforeRepositoryWrites(int kind)
    {
        using var f = new Fixture(); f.Export();
        var limits = kind switch
        {
            0 => new CombinationBundleLimits(MaxFiles: 1),
            1 => new CombinationBundleLimits(MaxTotalBytes: 1),
            2 => new CombinationBundleLimits(MaxManifestBytes: 1),
            _ => new CombinationBundleLimits(MaxArchiveBytes: 1)
        };
        Assert.Throws<InvalidDataException>(() => CombinationBundleService.PrepareImport(f.Zip, f.Destination, f.Staging, limits: limits));
        Assert.Empty(Directory.GetDirectories(f.Destination));
        Assert.Empty(Directory.GetDirectories(f.Staging));
    }

    [Fact]
    public void FileOccupyingModDirectory_BlocksImport()
    {
        using var f = new Fixture(); f.Export();
        Directory.CreateDirectory(Path.Combine(f.Destination, "角色"));
        File.WriteAllText(Path.Combine(f.Destination, "角色", "A"), "keep");
        using var import = f.Prepare(); Assert.False(import.CanInstall);
        Assert.Throws<InvalidDataException>(() => import.InstallFiles());
    }

    [Fact]
    public void PublicManifestCopy_CannotChangeInstalledState()
    {
        using var f = new Fixture(); f.Export(); using var import = f.Prepare();
        var copy = import.Manifest;
        ((IList<CombinationBundleMod>)copy.Mods).Clear();
        Assert.Equal(2, import.Manifest.Mods.Count);
        import.InstallFiles(); import.Complete();
        Assert.True(File.Exists(Path.Combine(f.Destination, "角色", "B", "mod.ini")));
        Assert.Throws<InvalidOperationException>(() => import.Complete());
    }

    [Fact]
    public void CancellationDuringExport_DeletesOwnedPartialOnly()
    {
        using var f = new Fixture(); using var cancellation = new CancellationTokenSource();
        string unrelated = Path.Combine(f.Root, "keep.partial"); File.WriteAllText(unrelated, "keep");
        Assert.Throws<OperationCanceledException>(() => CombinationBundleService.Export(f.Source, f.Zip, "test", "", false,
            [new("角色/A", []), new("角色/B", [])], cancellation.Token,
            progress: new InlineProgress(_ => cancellation.Cancel())));
        Assert.False(File.Exists(f.Zip)); Assert.Equal("keep", File.ReadAllText(unrelated));
        Assert.Empty(Directory.GetFiles(f.Root, ".imm-bundle-*.partial"));
    }

    [Fact]
    public void SourceEditDuringExport_RejectsSnapshot()
    {
        using var f = new Fixture();
        Assert.Throws<IOException>(() => CombinationBundleService.Export(f.Source, f.Zip, "test", "", false,
            [new("角色/A", []), new("角色/B", [])], progress: new InlineProgress(value =>
            {
                if (value.Percent == 95) File.AppendAllText(Path.Combine(f.Source, "角色", "A", "mod.ini"), "; changed");
            })));
        Assert.False(File.Exists(f.Zip)); Assert.Empty(Directory.GetFiles(f.Root, ".imm-bundle-*.partial"));
    }

    [Fact]
    public void CancellationDuringValidation_LeavesNoRepositoryOrStagedMods()
    {
        using var f = new Fixture(); f.Export(); using var cancellation = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => CombinationBundleService.PrepareImport(f.Zip, f.Destination, f.Staging,
            cancellation.Token, progress: new InlineProgress(_ => cancellation.Cancel())));
        Assert.Empty(Directory.GetFileSystemEntries(f.Destination)); Assert.Empty(Directory.GetFileSystemEntries(f.Staging));
    }

    [Fact]
    public void CancellationAfterFirstMove_RollsBackOnlyOwnedFolders()
    {
        using var f = new Fixture(); f.Export(); using var cancellation = new CancellationTokenSource();
        using (var import = f.Prepare())
        {
            Assert.Throws<OperationCanceledException>(() => import.InstallFiles(cancellation.Token,
                new InlineProgress(_ => cancellation.Cancel())));
            Assert.True(Directory.Exists(Path.Combine(f.Destination, "角色", "A")));
        }
        Assert.Empty(Directory.GetFileSystemEntries(f.Destination)); Assert.Empty(Directory.GetFileSystemEntries(f.Staging));
    }

    [Fact]
    public void CategoryFileCollision_RollsBackEarlierMove()
    {
        using var f = new Fixture();
        Directory.Move(Path.Combine(f.Source, "角色", "B"), Path.Combine(f.Source, "OtherB"));
        Directory.CreateDirectory(Path.Combine(f.Source, "second"));
        Directory.Move(Path.Combine(f.Source, "OtherB"), Path.Combine(f.Source, "second", "B"));
        CombinationBundleService.Export(f.Source, f.Zip, "test", "", false, [new("角色/A", []), new("second/B", [])]);
        using (var import = f.Prepare()) Assert.Throws<IOException>(() => import.InstallFiles(progress: new InlineProgress(_ =>
            File.WriteAllText(Path.Combine(f.Destination, "second"), "keep"))));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(f.Destination, "second")));
        Assert.False(Directory.Exists(Path.Combine(f.Destination, "角色"))); Assert.Empty(Directory.GetDirectories(f.Staging));
    }

    [Fact]
    public void StagingContainer_IsSeparateReusableAndRejectsOccupiedPath()
    {
        using var f = new Fixture();
        string staging = CombinationBundleService.CreateStagingParent(f.Destination);
        Assert.Equal(Path.Combine(f.Root, ".imm-bundle-staging"), staging);
        Assert.Equal(staging, CombinationBundleService.CreateStagingParent(f.Destination));
        Assert.Throws<InvalidDataException>(() => CombinationBundleService.CreateStagingParent(staging));
        Directory.Delete(staging); File.WriteAllText(staging, "keep");
        Assert.Throws<IOException>(() => CombinationBundleService.CreateStagingParent(f.Destination));
        Assert.Equal("keep", File.ReadAllText(staging));
    }

    [Fact]
    public void CorruptZip_RejectsBeforeRepositoryWrites()
    {
        using var f = new Fixture(); File.WriteAllText(f.Zip, "not a ZIP");
        Assert.Throws<InvalidDataException>(() => f.Prepare()); Assert.Empty(Directory.GetFileSystemEntries(f.Destination));
    }

    [Fact]
    public void StagingNestedInsideRepository_RejectsBeforeExtraction()
    {
        using var f = new Fixture(); f.Export(); string nested = Path.Combine(f.Destination, "temp"); Directory.CreateDirectory(nested);
        Assert.Throws<InvalidDataException>(() => CombinationBundleService.PrepareImport(f.Zip, f.Destination, nested));
        Assert.Empty(Directory.GetFileSystemEntries(nested));
    }

    [Fact]
    public void ManifestFileDirectoryCollision_RejectsBeforeExtraction()
    {
        using var f = new Fixture(); f.Export();
        f.ChangeManifest(node => node["mods"]![0]!["files"]!.AsArray().Add(new JsonObject
        {
            ["path"] = "mod.ini/child", ["length"] = 0, ["sha256"] = new string('0', 64)
        }));
        Assert.Throws<InvalidDataException>(() => f.Prepare()); Assert.Empty(Directory.GetFileSystemEntries(f.Staging));
    }

    [Theory]
    [InlineData(-1, 100000, 1)]
    [InlineData(2049, 100000, 1)]
    [InlineData(1, -1, 1)]
    [InlineData(1, 100001, 1)]
    [InlineData(1, 1, 0)]
    public void InvalidLimits_AreRejected(int mods, int files, int manifest)
    {
        using var f = new Fixture();
        Assert.Throws<ArgumentOutOfRangeException>(() => f.Export(limits: new(MaxMods: mods, MaxFiles: files, MaxManifestBytes: manifest)));
    }

    [Fact]
    public void HighlyCompressiblePayload_ExportedBundleCanBeImported()
    {
        using var f = new Fixture();
        File.WriteAllBytes(Path.Combine(f.Source, "角色", "A", "zero-buffer.buf"), new byte[4 * 1024 * 1024]);
        f.Export(); using var import = f.Prepare();
        Assert.True(import.CanInstall); import.InstallFiles(); import.Complete();
        Assert.Equal(4 * 1024 * 1024, new FileInfo(Path.Combine(f.Destination, "角色", "A", "zero-buffer.buf")).Length);
    }

    [Fact]
    public void BasenameInAnotherCategory_IsAmbiguousAndBlocksInstall()
    {
        using var f = new Fixture(); f.Export();
        Directory.CreateDirectory(Path.Combine(f.Destination, "other", "A"));
        using var import = f.Prepare(); Assert.False(import.CanInstall);
        Assert.Throws<InvalidDataException>(() => import.InstallFiles());
        Assert.False(Directory.Exists(Path.Combine(f.Destination, "角色")));
    }

    [Fact]
    public void OccupiedCategory_IsBlockedDuringPreview()
    {
        using var f = new Fixture(); f.Export(); File.WriteAllText(Path.Combine(f.Destination, "角色"), "keep");
        using var import = f.Prepare(); Assert.False(import.CanInstall);
        Assert.All(import.Preview, item => Assert.Equal(CombinationBundleDisposition.Conflict, item.Disposition));
    }

    [Fact]
    public void LinkedModFolder_ExportAndImportRejectWithoutFollowingLink()
    {
        using var f = new Fixture();
        string linked = Path.Combine(f.Source, "角色", "LinkedA");
        DirectoryLinkDeployment.CreateJunction(linked, Path.Combine(f.Source, "角色", "A"));
        try
        {
            Assert.Throws<InvalidDataException>(() => CombinationBundleService.Export(f.Source, f.Zip, "test", "", false, [new("角色/LinkedA", [])]));
            f.Export();
            string destination = Path.Combine(f.Destination, "角色"); Directory.CreateDirectory(destination);
            string destinationLink = Path.Combine(destination, "A");
            DirectoryLinkDeployment.CreateJunction(destinationLink, Path.Combine(f.Source, "角色", "A"));
            try { Assert.Throws<InvalidDataException>(() => f.Prepare()); }
            finally { Directory.Delete(destinationLink); }
            Assert.Equal(Fixture.Ini, File.ReadAllText(Path.Combine(f.Source, "角色", "A", "mod.ini")));
        }
        finally { Directory.Delete(linked); }
    }

    private sealed class InlineProgress(Action<CombinationBundleProgress> action) : IProgress<CombinationBundleProgress>
    {
        public void Report(CombinationBundleProgress value) => action(value);
    }

    private sealed class Fixture : IDisposable
    {
        public const string Ini = "[Constants]\nglobal persist $x = 0\n";
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "imm-bundle-tests-" + Guid.NewGuid().ToString("N"));
        public string Source => Path.Combine(Root, "source");
        public string Destination => Path.Combine(Root, "destination");
        public string Target => Path.Combine(Root, "target");
        public string Staging => Path.Combine(Root, "staging");
        public string Zip => Path.Combine(Root, "组合预设.zip");
        public string User => Path.Combine(Root, "d3dx_user.ini");
        public Fixture()
        {
            foreach (string path in new[] { Source, Destination, Target, Staging }) Directory.CreateDirectory(path);
            foreach (string mod in new[] { "A", "B" })
            {
                string path = Path.Combine(Source, "角色", mod); Directory.CreateDirectory(path);
                File.WriteAllText(Path.Combine(path, "mod.ini"), Ini);
            }
        }
        public void Export(string? output = null, bool state = true, CancellationToken token = default, CombinationBundleLimits? limits = null)
            => CombinationBundleService.Export(Source, output ?? Zip, "共享组合", "Test Game", state,
                new[] { "A", "B" }.Select((mod, i) => new CombinationPresetState("角色\\" + mod,
                    state ? [new("mod.ini", "$x", $"$\\Mods\\{mod}\\mod.ini\\x", (i + 1).ToString())] : [])).ToArray(), token, limits);
        public CombinationBundleImport Prepare() => CombinationBundleService.PrepareImport(Zip, Destination, Staging);
        public void AddExisting(string mod, string text)
        {
            string path = Path.Combine(Destination, "角色", mod); Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "mod.ini"), text);
        }
        public void AddEntry(string name, string text)
        {
            using var zip = ZipFile.Open(Zip, ZipArchiveMode.Update);
            using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write(text);
        }
        public void ChangeManifest(Action<JsonNode> change) => RewriteManifest(text =>
        {
            JsonNode node = JsonNode.Parse(text)!; change(node); return node.ToJsonString();
        });
        public void RewriteManifest(Func<string, string> change)
        {
            using var zip = ZipFile.Open(Zip, ZipArchiveMode.Update);
            var entry = zip.GetEntry("preset.json")!;
            string text;
            using (var reader = new StreamReader(entry.Open())) text = reader.ReadToEnd();
            entry.Delete();
            using var writer = new StreamWriter(zip.CreateEntry("preset.json").Open(), new UTF8Encoding(false));
            writer.Write(change(text));
        }
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
}
