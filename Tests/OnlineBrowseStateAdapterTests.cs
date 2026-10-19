using System.Collections.ObjectModel;
using System.Collections.Specialized;
using ModFolderCopier.WinUI;

namespace IntegratedModManager.Core.Tests;

public sealed class OnlineBrowseStateAdapterTests
{
    private sealed class Card(int id)
    {
        public int Id { get; } = id;
        public string Status { get; set; } = string.Empty;
    }

    [Fact]
    public void DownloadLifecycle_DoesNotResetOrReplaceScrolledItems()
    {
        Card[] cards = Enumerable.Range(1, 50).Select(id => new Card(id)).ToArray();
        var visible = new ObservableCollection<Card>(cards);
        var changes = new List<NotifyCollectionChangedAction>();
        visible.CollectionChanged += (_, e) => changes.Add(e.Action);
        foreach (string state in new[] { "preparing", "downloading", "extracting", "completed", "failed", "canceled" })
        {
            cards[35].Status = state;
            Assert.False(OnlineBrowseStateAdapter.Synchronize(visible, cards.ToList()));
            Assert.Same(cards[35], visible[35]);
        }
        Assert.Empty(changes);
    }

    [Fact]
    public void SortAndFilter_KeepSurvivingInstances_WithoutReset()
    {
        var a = new Card(1); var b = new Card(2); var c = new Card(3); var d = new Card(4);
        var visible = new ObservableCollection<Card>([a, b, c]);
        var changes = new List<NotifyCollectionChangedAction>();
        visible.CollectionChanged += (_, e) => changes.Add(e.Action);
        Assert.True(OnlineBrowseStateAdapter.Synchronize(visible, new[] { c, a, d }));
        Assert.Equal(new[] { c, a, d }, visible);
        Assert.Contains(NotifyCollectionChangedAction.Move, changes);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, changes);
        Assert.False(OnlineBrowseStateAdapter.Synchronize(visible, new[] { c, a, d }));
    }

    [Fact]
    public void ReloadWithNewMetadata_UsesNewInstances()
    {
        var old = new Card(1); var updated = new Card(1);
        var visible = new ObservableCollection<Card>([old]);
        Assert.True(OnlineBrowseStateAdapter.Synchronize(visible, new[] { updated }));
        Assert.Same(updated, Assert.Single(visible));
    }

    [Fact]
    public void EmptyResults_AndReturningFromAuthorFilter_AreSupported()
    {
        Card[] cards = [new(1), new(2)];
        var visible = new ObservableCollection<Card>(cards);
        Assert.True(OnlineBrowseStateAdapter.Synchronize(visible, Array.Empty<Card>()));
        Assert.Empty(visible);
        Assert.False(OnlineBrowseStateAdapter.Synchronize(visible, Array.Empty<Card>()));
        Assert.True(OnlineBrowseStateAdapter.Synchronize(visible, cards));
        Assert.Equal(cards, visible);
    }

    [Theory]
    [InlineData("alice", true)]
    [InlineData(" ALICE WORKS ", true)]
    [InlineData("author:alice", true)]
    [InlineData("AUTHOR: Alice Works", true)]
    [InlineData("author: coat", false)]
    [InlineData("author: amber", false)]
    [InlineData("author:", false)]
    [InlineData("author:missing", false)]
    [InlineData("coat", true)]
    [InlineData("amber", true)]
    [InlineData("安柏", true)]
    [InlineData("", true)]
    public void Search_MatchesAuthorsTitlesCharactersAndAliases(string term, bool expected)
        => Assert.Equal(expected, OnlineBrowseStateAdapter.MatchesSearch(
            "Winter Coat", "Amber", "Alice Works", term, ["安柏"]));

    [Theory]
    [InlineData("画师甲", "author:画师甲")]
    [InlineData("A:B Studios", "author:A:B Studios")]
    public void AuthorNames_WithUnicodeAndPunctuation_AreLiteral(string author, string term)
        => Assert.True(OnlineBrowseStateAdapter.MatchesSearch(null, null, author, term));

    [Fact]
    public void MissingMetadata_DoesNotMatchNonemptySearch()
    {
        Assert.False(OnlineBrowseStateAdapter.MatchesSearch(null, null, null, "alice"));
        Assert.False(OnlineBrowseStateAdapter.MatchesSearch(null, null, null, "author:alice"));
        Assert.True(OnlineBrowseStateAdapter.MatchesSearch(null, null, null, null));
    }
}
