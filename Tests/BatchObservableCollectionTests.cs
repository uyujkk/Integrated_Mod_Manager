using System.Collections.Specialized;
using System.ComponentModel;
using IntegratedModManager.Core;

namespace IntegratedModManager.Core.Tests;

public sealed class BatchObservableCollectionTests
{
    [Theory]
    [InlineData(1)] [InlineData(100)] [InlineData(500)] [InlineData(1000)]
    public void SnapshotReplacementEmitsOnlyOneReset(int count)
    {
        var collection = new BatchObservableCollection<object>();
        int events = 0;
        collection.CollectionChanged += (_, e) => { events++; Assert.Equal(NotifyCollectionChangedAction.Reset, e.Action); };
        object[] values = Enumerable.Range(0, count).Select(_ => new object()).ToArray();
        Assert.True(collection.ReplaceAll(values)); Assert.Equal(1, events); Assert.Equal(values, collection);
        Assert.False(collection.ReplaceAll(values)); Assert.Equal(1, events);
        Assert.Equal(count > 1, collection.ReplaceAll(values.Reverse()));
    }
    [Fact]
    public void EmptyAndSelfSnapshotsAreNoOps()
    {
        var collection = new BatchObservableCollection<int>();
        Assert.False(collection.ReplaceAll([]));
        collection.Add(1);
        int changes = 0; collection.CollectionChanged += (_, _) => changes++;
        Assert.False(collection.ReplaceAll(collection)); Assert.Equal(0, changes);
        Assert.True(collection.ReplaceAll([])); Assert.Equal(1, changes); Assert.Empty(collection);
    }
    [Fact]
    public void FailedEnumerationAndNullInputsKeepPreviousDisplay()
    {
        var collection = new BatchObservableCollection<int>(); collection.Add(7);
        int changes = 0; collection.CollectionChanged += (_, _) => changes++;
        Assert.Throws<ArgumentNullException>(() => collection.ReplaceAll(null!));
        Assert.Throws<IOException>(() => collection.ReplaceAll(Fail()));
        Assert.Equal(7, Assert.Single(collection)); Assert.Equal(0, changes);
        static IEnumerable<int> Fail() { yield return 1; throw new IOException("test"); }
    }
    [Fact]
    public void ReplacingSameCountStillReportsIndexerButNotCount()
    {
        var collection = new BatchObservableCollection<int>(); collection.Add(7);
        List<string?> changes = [];
        ((INotifyPropertyChanged)collection).PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        Assert.True(collection.ReplaceAll([8])); Assert.Equal(["Item[]"], changes);
        changes.Clear(); collection.ReplaceAll([8, 9]); Assert.Equal(["Count", "Item[]"], changes);
    }
    [Fact]
    public void OrdinaryEditsKeepObservableCollectionNotifications()
    {
        var collection = new BatchObservableCollection<int>();
        List<NotifyCollectionChangedAction> actions = [];
        collection.CollectionChanged += (_, e) => actions.Add(e.Action);
        collection.Add(1); collection.Insert(0, 2); collection[0] = 3; collection.Move(0, 1); collection.Remove(1); collection.Clear();
        Assert.Equal([NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Replace,
            NotifyCollectionChangedAction.Move, NotifyCollectionChangedAction.Remove, NotifyCollectionChangedAction.Reset], actions);
    }
    [Fact]
    public void DuplicateValuesAndItemIdentityAreNotChanged()
    {
        var collection = new BatchObservableCollection<object?>(); object item = new();
        collection.ReplaceAll([item, item, null]);
        Assert.Same(item, collection[0]); Assert.Same(item, collection[1]); Assert.Null(collection[2]);
        collection.ReplaceAll([item]); Assert.Same(item, Assert.Single(collection));
    }
    [Fact]
    public void MultipleSubscribersRejectReentrantSnapshotMutation()
    {
        var collection = new BatchObservableCollection<int>();
        collection.CollectionChanged += (_, _) => Assert.Throws<InvalidOperationException>(() => collection.ReplaceAll([2]));
        collection.CollectionChanged += (_, _) => { };
        collection.ReplaceAll([1]); Assert.Equal(1, Assert.Single(collection));
    }
}
