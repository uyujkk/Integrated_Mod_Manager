using IntegratedModManager.Core.Online;
using IntegratedModManager.Core.Repository;

namespace IntegratedModManager.Core.Tests;

public sealed class CharacterFolderAvatarsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "imm-avatars-" + Guid.NewGuid().ToString("N"));
    private static readonly Dictionary<string, string> NoBindings = new(StringComparer.OrdinalIgnoreCase);

    private static readonly OnlineCharacterOption HuTao = new()
    {
        Key = "Hu Tao", DisplayNameZh = "胡桃", DisplayNameEn = "Hu Tao", AvatarUrl = "https://img/hutao.png", Aliases = ["往生堂堂主"]
    };
    private static readonly OnlineCharacterOption Raiden = new()
    {
        Key = "Raiden Shogun", DisplayNameZh = "雷电将军", DisplayNameEn = "Raiden Shogun", AvatarUrl = "https://img/raiden.png", Aliases = ["Raiden", "Ei"]
    };
    private static readonly OnlineCharacterOption RaidenMei = new()
    {
        Key = "Raiden Mei", DisplayNameZh = "雷电芽衣", DisplayNameEn = "Raiden Mei", Aliases = ["Raiden"]
    };
    private static readonly IReadOnlyList<OnlineCharacterOption> Catalog = [HuTao, Raiden];

    public CharacterFolderAvatarsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Theory]
    [InlineData("胡桃")]
    [InlineData("Hu Tao")]
    [InlineData("hutao")]
    [InlineData("ＨＵ＿ＴＡＯ")]
    [InlineData("hu-tao")]
    [InlineData("往生堂堂主")]
    public void Match_FindsTheCharacterByAnyNameAfterNormalization(string folderName)
    {
        Assert.Same(HuTao, CharacterFolderAvatars.Match(folderName, Catalog));
    }

    [Theory]
    [InlineData("Hu")]
    [InlineData("胡桃 皮肤")]
    [InlineData("---")]
    [InlineData("")]
    public void Match_DoesNotGuessFromPartialNames(string folderName)
    {
        Assert.Null(CharacterFolderAvatars.Match(folderName, Catalog));
    }

    [Fact]
    public void Match_PrefersKeysAndNamesAndRejectsAmbiguousAliases()
    {
        Assert.Null(CharacterFolderAvatars.Match("Raiden", [Raiden, RaidenMei]));
        Assert.Same(RaidenMei, CharacterFolderAvatars.Match("Raiden Mei", [Raiden, RaidenMei]));
        Assert.Same(Raiden, CharacterFolderAvatars.Match("Ei", [Raiden, RaidenMei]));
    }

    [Fact]
    public void Resolve_UsesCustomFileThenBindingThenMatch()
    {
        string folder = Folder("Ei");
        Assert.Equal(new CharacterAvatar(CharacterAvatarSource.MatchedCharacter, null, Raiden), CharacterFolderAvatars.Resolve(folder, Catalog, NoBindings));

        var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["ei"] = "hu tao" };
        CharacterAvatar bound = CharacterFolderAvatars.Resolve(folder, Catalog, bindings);
        Assert.Equal((CharacterAvatarSource.BoundCharacter, "https://img/hutao.png"), (bound.Source, bound.ImageUrl));

        File.WriteAllBytes(Path.Combine(folder, "avatar.jpg"), [1]);
        File.WriteAllBytes(Path.Combine(folder, ".imm_avatar.png"), [2]);
        CharacterAvatar custom = CharacterFolderAvatars.Resolve(folder, Catalog, bindings);
        Assert.Equal((CharacterAvatarSource.CustomFile, Path.Combine(folder, ".imm_avatar.png")), (custom.Source, custom.FilePath));
        Assert.Null(custom.ImageUrl);
    }

    [Fact]
    public void Resolve_IgnoresBindingsToMissingCharactersAndUnknownFolders()
    {
        var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Somebody"] = "Missing" };

        Assert.Same(CharacterAvatar.None, CharacterFolderAvatars.Resolve(Folder("Somebody"), Catalog, bindings));
        Assert.Same(CharacterAvatar.None, CharacterFolderAvatars.Resolve(Path.Combine(_root, "absent"), Catalog, NoBindings));
    }

    [Fact]
    public void FindCustomFile_AcceptsUserNamedAvatarFiles()
    {
        string folder = Folder("A");
        Assert.Null(CharacterFolderAvatars.FindCustomFile(folder));
        File.WriteAllBytes(Path.Combine(folder, "avatar.webp"), [1]);
        File.WriteAllBytes(Path.Combine(folder, "avatar.txt"), [1]);

        Assert.Equal(Path.Combine(folder, "avatar.webp"), CharacterFolderAvatars.FindCustomFile(folder));
    }

    [Fact]
    public void SetCustomAvatar_CopiesTheImageAndKeepsThePreviousOne()
    {
        string folder = Folder("A");
        string first = Image("first.png", 1);
        string second = Image("second.JPG", 2);

        Assert.Equal(Path.Combine(folder, ".imm_avatar.png"), CharacterFolderAvatars.SetCustomAvatar(folder, first));
        Assert.Equal(Path.Combine(folder, ".imm_avatar.jpg"), CharacterFolderAvatars.SetCustomAvatar(folder, second));

        Assert.Equal([2], File.ReadAllBytes(Path.Combine(folder, ".imm_avatar.jpg")));
        Assert.Equal([1], File.ReadAllBytes(Path.Combine(folder, ".imm_avatar.previous.png")));
        Assert.False(File.Exists(Path.Combine(folder, ".imm_avatar.png")));
        Assert.True(File.Exists(first));
        Assert.Equal(2, Directory.GetFiles(folder).Length);
    }

    [Fact]
    public void RestorePreviousAvatar_SwapsCurrentAndPrevious()
    {
        string folder = Folder("A");
        CharacterFolderAvatars.SetCustomAvatar(folder, Image("first.png", 1));
        CharacterFolderAvatars.SetCustomAvatar(folder, Image("second.gif", 2));

        Assert.True(CharacterFolderAvatars.RestorePreviousAvatar(folder));

        Assert.Equal([1], File.ReadAllBytes(Path.Combine(folder, ".imm_avatar.png")));
        Assert.Equal([2], File.ReadAllBytes(Path.Combine(folder, ".imm_avatar.previous.gif")));
        Assert.Equal(2, Directory.GetFiles(folder).Length);
    }

    [Fact]
    public void RemoveCustomAvatar_KeepsItAsThePreviousBackup()
    {
        string folder = Folder("A");
        Assert.False(CharacterFolderAvatars.RemoveCustomAvatar(folder));
        Assert.False(CharacterFolderAvatars.RestorePreviousAvatar(folder));
        CharacterFolderAvatars.SetCustomAvatar(folder, Image("first.bmp", 1));

        Assert.True(CharacterFolderAvatars.RemoveCustomAvatar(folder));

        Assert.Null(CharacterFolderAvatars.FindCustomFile(folder));
        Assert.Equal([1], File.ReadAllBytes(Path.Combine(folder, ".imm_avatar.previous.bmp")));
    }

    [Fact]
    public void SetCustomAvatar_RejectsUnsupportedFilesAndMissingFolders()
    {
        Assert.Throws<InvalidDataException>(() => CharacterFolderAvatars.SetCustomAvatar(Folder("A"), Image("notes.txt", 1)));
        Assert.Throws<DirectoryNotFoundException>(() => CharacterFolderAvatars.SetCustomAvatar(Path.Combine(_root, "absent"), Image("a.png", 1)));
        Assert.Throws<InvalidDataException>(() => CharacterFolderAvatars.SetCustomAvatar(Folder("B"), Image("empty.png", 0)));
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "B")));
    }

    private string Folder(string name) => Directory.CreateDirectory(Path.Combine(_root, name)).FullName;

    private string Image(string name, int content)
    {
        string directory = Directory.CreateDirectory(Path.Combine(_root, "source")).FullName;
        string path = Path.Combine(directory, name);
        File.WriteAllBytes(path, content == 0 ? [] : [(byte)content]);
        return path;
    }
}
