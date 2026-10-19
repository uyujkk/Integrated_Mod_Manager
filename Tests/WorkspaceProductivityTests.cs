using System.Text.Json;
using IntegratedModManager.Core;

namespace IntegratedModManager.Core.Tests;

public sealed class WorkspaceProductivityTests
{
    [Fact]
    public void BrowseSessions_AreScopedToRepositoryLocationSourceAndGame()
    {
        string root = Path.GetFullPath("repo-a");
        string baseline = WorkspaceIdentity.Online("same-id", root, "GameBanana", "42");
        Assert.Equal(baseline, WorkspaceIdentity.Online("same-id", root + Path.DirectorySeparatorChar, "gamebanana", "42"));
        Assert.NotEqual(baseline, WorkspaceIdentity.Online("same-id", Path.GetFullPath("repo-b"), "GameBanana", "42"));
        Assert.NotEqual(baseline, WorkspaceIdentity.Online("same-id", root, "GameBanana", "43"));
        Assert.NotEqual(baseline, WorkspaceIdentity.Online("different-id", root, "GameBanana", "42"));
        Assert.NotEqual(baseline, WorkspaceIdentity.Online("same-id", root, "Other", "42"));
        Assert.NotEqual(WorkspaceIdentity.Library("a", root), WorkspaceIdentity.Library("a", Path.GetFullPath("repo-b")));
    }

    [Fact]
    public void SessionRoundTrip_RetainsAuthorPageSearchLayoutAndAnchor()
    {
        var state = new OnlineBrowseSession { Page = 3, AuthorId = 42, AuthorName = "Alice", Character = "Role", Search = "test",
            Sort = "Updated", Layout = "Grid", WorkFilter = 2, Offset = 2015, Anchor = "GameBanana:7", AnchorOffset = -35 };
        var restored = JsonSerializer.Deserialize<OnlineBrowseSession>(JsonSerializer.Serialize(state))!.Sanitize();
        Assert.Equal(JsonSerializer.Serialize(state), JsonSerializer.Serialize(restored));
        state.Page = 9;
        Assert.Equal(3, restored.Page);
    }

    [Fact]
    public void MalformedSessions_CannotRestoreUnboundedOrInvalidViewState()
    {
        var restored = new OnlineBrowseSession { Page = -1, AuthorId = -42, Search = new string('s', 2000), Sort = "invalid", Layout = "invalid",
            Character = null!, AuthorName = null!, Anchor = null!, WorkFilter = 99, Offset = double.NaN, AnchorOffset = double.PositiveInfinity }.Sanitize();
        Assert.Equal(1, restored.Page); Assert.Equal(0, restored.AuthorId); Assert.Equal(1024, restored.Search.Length);
        Assert.Equal("Hotness", restored.Sort); Assert.Equal("List", restored.Layout); Assert.Equal(2, restored.WorkFilter);
        Assert.Equal(0, restored.Offset); Assert.Equal(0, restored.AnchorOffset); Assert.Empty(restored.Character);
        Assert.Equal(100000, new OnlineBrowseSession { Page = int.MaxValue }.Sanitize().Page);
        Assert.Equal(100000000, new OnlineBrowseSession { Offset = double.MaxValue }.Sanitize().Offset);
    }

    [Fact]
    public void RelativePaths_CannotEscapeRepository_AndNormalizeSeparators()
    {
        string root = Path.GetFullPath("repo");
        Assert.Equal("Role/Mod", WorkspaceIdentity.Relative(root, Path.Combine(root, "Role", "Mod")));
        Assert.Throws<ArgumentException>(() => WorkspaceIdentity.Relative(root, Path.GetFullPath("other/Mod")));
        Assert.Throws<ArgumentException>(() => WorkspaceIdentity.Relative(root, Path.GetDirectoryName(root)!));
    }

    [Fact]
    public void BulkTags_AddRemoveReplaceAndClear_DoNotShareMutableLists()
    {
        var library = new LocalLibraryState();
        library.EditTags(["Role/A", "Role/B"], ["Favorite", "To try", "favorite"], TagEditMode.Add);
        library.EditTags(["role/a"], ["FAVORITE"], TagEditMode.Remove);
        Assert.Equal(["To try"], library.TagsFor("Role/A"));
        Assert.Equal(["Favorite", "To try"], library.TagsFor("Role/B"));
        library.EditTags(["Role/A"], ["Broken"], TagEditMode.Replace);
        Assert.Equal(["Broken"], library.TagsFor("Role/A"));
        library.EditTags(["Role/A"], [], TagEditMode.Replace);
        Assert.False(library.ModTags.ContainsKey("Role/A"));
        Assert.Equal(["Favorite", "To try"], library.TagsFor("Role/B"));
    }

    [Fact]
    public void TagNormalization_BoundsLabelsAndCounts()
    {
        Assert.Empty(LocalLibraryState.NormalizeTags(null));
        Assert.Equal(["Valid"], LocalLibraryState.NormalizeTags(["", " ", " Valid ", "valid", new string('a', 33), "bad\ntag"]));
        Assert.Equal(16, LocalLibraryState.NormalizeTags(Enumerable.Range(0, 30).Select(i => i.ToString())).Length);
    }

    [Fact]
    public void TagAndTextFilters_Combine_AndSavedFiltersRoundTripWithoutAliasing()
    {
        var library = new LocalLibraryState();
        library.EditTags(["Role/A"], ["Favorite", "To try"], TagEditMode.Add);
        Assert.True(library.Matches("Role/A", "Mod A", "TRY", "favorite"));
        Assert.True(library.Matches("Role/A", "Mod A", "mod", ""));
        Assert.False(library.Matches("Role/A", "Mod A", "absent", "favorite"));
        Assert.False(library.Matches("Role/B", "Mod B", "", "favorite"));
        Assert.True(library.Matches("Role/B", "Mod B", null, null));
        var filter = new LocalModFilter { Name = "Try", Character = "Role", Search = "try", Tag = "Favorite" };
        library.SaveFilter(filter); filter.Tag = "changed";
        Assert.Equal("Favorite", library.Filters[0].Tag);
        var restored = JsonSerializer.Deserialize<LocalLibraryState>(JsonSerializer.Serialize(library))!;
        restored.Normalize();
        Assert.True(restored.Matches("role/a", "Mod", "try", "favorite"));
        Assert.Equal("Role", restored.Filters[0].Character);
        library.SaveFilter(new LocalModFilter { Name = "try", Search = "updated" });
        Assert.Single(library.Filters); Assert.Equal("updated", library.Filters[0].Search);
    }

    [Fact]
    public void RoleRename_MovesTagsAndSavedRoleReferences_WithoutTouchingOtherRoles()
    {
        var library = new LocalLibraryState();
        library.EditTags(["Old/A", "Older/B"], ["Favorite"], TagEditMode.Add);
        library.SaveFilter(new LocalModFilter { Name = "one", Character = "old" });
        library.RenameCharacter("Old", "New");
        Assert.Empty(library.TagsFor("Old/A")); Assert.Equal(["Favorite"], library.TagsFor("New/A"));
        Assert.Equal(["Favorite"], library.TagsFor("Older/B")); Assert.Equal("New", library.Filters[0].Character);
    }

    [Fact]
    public void SavedFilters_AreBoundedAndRejectBlankOrLongNames()
    {
        var library = new LocalLibraryState();
        Assert.Throws<ArgumentException>(() => library.SaveFilter(new LocalModFilter { Name = " " }));
        Assert.Throws<ArgumentException>(() => library.SaveFilter(new LocalModFilter { Name = new string('n', 65) }));
        for (int i = 0; i < 60; i++) library.SaveFilter(new LocalModFilter { Name = i.ToString() });
        Assert.Equal(50, library.Filters.Count); Assert.Equal("59", library.Filters[0].Name);
        library.Filters.Add(new LocalModFilter { Name = "59" }); library.Filters.Add(new LocalModFilter());
        library.Normalize(); Assert.Equal(50, library.Filters.Count);
        new LocalLibraryState { ModTags = null!, Filters = null! }.Normalize();
    }
}
