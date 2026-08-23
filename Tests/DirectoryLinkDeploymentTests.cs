using IntegratedModManager.Core;

namespace IntegratedModManager.Core.Tests;

public sealed class DirectoryLinkDeploymentTests
{
    [Fact]
    public void Junction_WriteThroughUpdatesRepositoryAndRemovalKeepsTarget()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var directory = new TemporaryDirectory();
        string repositoryMod = Path.Combine(directory.Path, "repository", "ExampleMod");
        string loaderMods = Path.Combine(directory.Path, "loader", "Mods");
        string junction = Path.Combine(loaderMods, "ExampleMod");
        Directory.CreateDirectory(repositoryMod);
        Directory.CreateDirectory(loaderMods);
        File.WriteAllText(Path.Combine(repositoryMod, "settings.ini"), "state=1");

        DirectoryLinkDeployment.CreateJunction(junction, repositoryMod);
        File.WriteAllText(Path.Combine(junction, "settings.ini"), "state=2");

        Assert.True(DirectoryLinkDeployment.IsDirectoryLink(junction));
        Assert.Equal("state=2", File.ReadAllText(Path.Combine(repositoryMod, "settings.ini")));
        Assert.Equal(Path.GetFullPath(repositoryMod), DirectoryLinkDeployment.TryGetTarget(junction), ignoreCase: true);

        DirectoryLinkDeployment.RemoveLink(junction);

        Assert.False(Directory.Exists(junction));
        Assert.True(Directory.Exists(repositoryMod));
        Assert.Equal("state=2", File.ReadAllText(Path.Combine(repositoryMod, "settings.ini")));
    }

    [Fact]
    public void CreateJunction_RejectsExistingDestination()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var directory = new TemporaryDirectory();
        string target = Path.Combine(directory.Path, "target");
        string junction = Path.Combine(directory.Path, "junction");
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(junction);

        Assert.Throws<IOException>(() => DirectoryLinkDeployment.CreateJunction(junction, target));
    }

    [Fact]
    public void Junction_CanBeMovedForTransactionalRollbackWithoutMovingTarget()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var directory = new TemporaryDirectory();
        string target = Path.Combine(directory.Path, "repository", "ModA");
        string loader = Path.Combine(directory.Path, "loader");
        string junction = Path.Combine(loader, "ModA");
        string displaced = Path.Combine(loader, ".ModA.previous");
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(loader);
        File.WriteAllText(Path.Combine(target, "mod.ini"), "enabled=1");
        DirectoryLinkDeployment.CreateJunction(junction, target);

        Directory.Move(junction, displaced);

        Assert.False(Directory.Exists(junction));
        Assert.True(DirectoryLinkDeployment.IsDirectoryLink(displaced));
        Assert.True(File.Exists(Path.Combine(displaced, "mod.ini")));
        Assert.True(File.Exists(Path.Combine(target, "mod.ini")));

        Directory.Move(displaced, junction);

        Assert.True(DirectoryLinkDeployment.IsDirectoryLink(junction));
        DirectoryLinkDeployment.RemoveLink(junction);
        Assert.True(Directory.Exists(target));
    }

    [Fact]
    public void RemoveLink_RefusesNormalDirectory()
    {
        using var directory = new TemporaryDirectory();
        string normalDirectory = Path.Combine(directory.Path, "normal");
        Directory.CreateDirectory(normalDirectory);

        Assert.Throws<InvalidOperationException>(() => DirectoryLinkDeployment.RemoveLink(normalDirectory));
        Assert.True(Directory.Exists(normalDirectory));
    }

    [Fact]
    public void RemoveLink_MissingPathIsNoOp()
    {
        using var directory = new TemporaryDirectory();

        DirectoryLinkDeployment.RemoveLink(Path.Combine(directory.Path, "missing-link"));
    }

    [Fact]
    public void CreateJunction_RejectsPathInsideTarget()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var directory = new TemporaryDirectory();
        string target = Path.Combine(directory.Path, "target");
        Directory.CreateDirectory(target);

        Assert.Throws<InvalidOperationException>(() =>
            DirectoryLinkDeployment.CreateJunction(Path.Combine(target, "loop"), target));
    }

    [Fact]
    public void FindSiblingLinksToReplace_ReturnsOnlyLinkedModForSameCharacter()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var directory = new TemporaryDirectory();
        string repository = Path.Combine(directory.Path, "repository");
        string modA = Path.Combine(repository, "CharacterA", "ModA");
        string modB = Path.Combine(repository, "CharacterA", "ModB");
        string otherCharacterMod = Path.Combine(repository, "CharacterB", "ModC");
        string loader = Path.Combine(directory.Path, "loader", "Mods");
        Directory.CreateDirectory(modA);
        Directory.CreateDirectory(modB);
        Directory.CreateDirectory(otherCharacterMod);
        Directory.CreateDirectory(loader);
        string modALink = Path.Combine(loader, "ModA");
        string otherCharacterLink = Path.Combine(loader, "ModC");
        DirectoryLinkDeployment.CreateJunction(modALink, modA);
        DirectoryLinkDeployment.CreateJunction(otherCharacterLink, otherCharacterMod);

        IReadOnlyList<string> result = DirectoryLinkDeployment.FindSiblingLinksToReplace(modB, repository, loader);

        Assert.Single(result);
        Assert.Equal(Path.GetFullPath(modALink), result[0], ignoreCase: true);
        Assert.DoesNotContain(Path.GetFullPath(otherCharacterLink), result, StringComparer.OrdinalIgnoreCase);

        DirectoryLinkDeployment.RemoveLink(modALink);
        DirectoryLinkDeployment.RemoveLink(otherCharacterLink);
    }

    [Fact]
    public void FindSiblingLinksToReplace_IgnoresNormalCopiedSibling()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var directory = new TemporaryDirectory();
        string repository = Path.Combine(directory.Path, "repository");
        string modA = Path.Combine(repository, "CharacterA", "ModA");
        string modB = Path.Combine(repository, "CharacterA", "ModB");
        string loader = Path.Combine(directory.Path, "loader", "Mods");
        Directory.CreateDirectory(modA);
        Directory.CreateDirectory(modB);
        Directory.CreateDirectory(Path.Combine(loader, "ModA"));

        IReadOnlyList<string> result = DirectoryLinkDeployment.FindSiblingLinksToReplace(modB, repository, loader);

        Assert.Empty(result);
        Assert.True(Directory.Exists(Path.Combine(loader, "ModA")));
    }

    [Fact]
    public void FindSiblingLinksToReplace_RejectsMissingSource()
    {
        using var directory = new TemporaryDirectory();
        string repository = Path.Combine(directory.Path, "repository");
        string loader = Path.Combine(directory.Path, "loader");
        Directory.CreateDirectory(repository);
        Directory.CreateDirectory(loader);

        Assert.Throws<DirectoryNotFoundException>(() =>
            DirectoryLinkDeployment.FindSiblingLinksToReplace(
                Path.Combine(repository, "Character", "MissingMod"), repository, loader));
    }

    [Fact]
    public void FindSiblingLinksToReplace_RejectsMissingRepository()
    {
        using var directory = new TemporaryDirectory();
        string source = Path.Combine(directory.Path, "source", "Character", "ModA");
        string loader = Path.Combine(directory.Path, "loader");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(loader);

        Assert.Throws<DirectoryNotFoundException>(() =>
            DirectoryLinkDeployment.FindSiblingLinksToReplace(
                source, Path.Combine(directory.Path, "missing-repository"), loader));
    }

    [Fact]
    public void FindSiblingLinksToReplace_RejectsMissingLoaderRoot()
    {
        using var directory = new TemporaryDirectory();
        string repository = Path.Combine(directory.Path, "repository");
        string source = Path.Combine(repository, "Character", "ModA");
        Directory.CreateDirectory(source);

        Assert.Throws<DirectoryNotFoundException>(() =>
            DirectoryLinkDeployment.FindSiblingLinksToReplace(
                source, repository, Path.Combine(directory.Path, "missing-loader")));
    }

    [Fact]
    public void FindSiblingLinksToReplace_RejectsSourceOutsideRepository()
    {
        using var directory = new TemporaryDirectory();
        string repository = Path.Combine(directory.Path, "repository");
        string source = Path.Combine(directory.Path, "elsewhere", "Character", "ModA");
        string loader = Path.Combine(directory.Path, "loader");
        Directory.CreateDirectory(repository);
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(loader);

        Assert.Throws<InvalidOperationException>(() =>
            DirectoryLinkDeployment.FindSiblingLinksToReplace(source, repository, loader));
    }

    [Fact]
    public void FindSiblingLinksToReplace_RequiresFirstLevelCharacterFolder()
    {
        using var directory = new TemporaryDirectory();
        string repository = Path.Combine(directory.Path, "repository");
        string source = Path.Combine(repository, "ModAtRepositoryRoot");
        string loader = Path.Combine(directory.Path, "loader");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(loader);

        Assert.Throws<InvalidOperationException>(() =>
            DirectoryLinkDeployment.FindSiblingLinksToReplace(source, repository, loader));
    }

    [Fact]
    public void FindSiblingLinksToReplace_IgnoresJunctionWithUnexpectedTarget()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var directory = new TemporaryDirectory();
        string repository = Path.Combine(directory.Path, "repository");
        string modA = Path.Combine(repository, "CharacterA", "ModA");
        string modB = Path.Combine(repository, "CharacterA", "ModB");
        string unexpectedTarget = Path.Combine(repository, "CharacterB", "Unexpected");
        string loader = Path.Combine(directory.Path, "loader", "Mods");
        Directory.CreateDirectory(modA);
        Directory.CreateDirectory(modB);
        Directory.CreateDirectory(unexpectedTarget);
        Directory.CreateDirectory(loader);
        string deployedPath = Path.Combine(loader, "ModA");
        DirectoryLinkDeployment.CreateJunction(deployedPath, unexpectedTarget);

        IReadOnlyList<string> result = DirectoryLinkDeployment.FindSiblingLinksToReplace(modB, repository, loader);

        Assert.Empty(result);
        DirectoryLinkDeployment.RemoveLink(deployedPath);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "imm-link-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (!Directory.Exists(Path))
            {
                return;
            }

            foreach (string child in Directory.GetDirectories(Path, "*", SearchOption.TopDirectoryOnly))
            {
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                {
                    Directory.Delete(child, recursive: false);
                }
            }
            Directory.Delete(Path, recursive: true);
        }
    }
}
