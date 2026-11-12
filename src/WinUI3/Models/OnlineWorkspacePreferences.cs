using System.Text.Json;
using System.Text.Json.Serialization;
using IntegratedModManager.Core.Online;
using IntegratedModManager.Core.Repository;

namespace ModFolderCopier.WinUI;

public sealed record OnlineAuthorBookmark(int Id, string Name, string CategoryId);
public sealed record OnlineSavedMod(string RepositoryId, string RepositoryPath, OnlineModCard Mod);
/// <summary>The Online page's category for one browse session (issue #129); empty IDs mean the characters.</summary>
public sealed class OnlineCategorySelection
{
    public string TopId { get; set; } = string.Empty;
    public string SubId { get; set; } = string.Empty;
    public bool AllOfCharacterTop { get; set; }
}

public sealed class OnlineWorkspacePreferences
{
    public Dictionary<string, OnlineBrowseSession> BrowseSessions { get; set; } = [];
    public List<OnlineAuthorVisit> RecentAuthors { get; set; } = [];
    public int ConcurrentDownloads { get; set; } = 2;
    public bool RememberDownloadFolders { get; set; }
    public Dictionary<string, string> DownloadFolders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<OnlineAuthorBookmark> Authors { get; set; } = [];
    public List<OnlineSavedMod> SavedMods { get; set; } = [];
    public string? LastImportManifest { get; set; }
    public Dictionary<string, OnlineCategorySelection> CategorySelections { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
