using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace IntegratedModManager.Core;

/// <summary>UI-thread-owned collection. Replace a snapshot with at most one Reset,
/// preserving normal ObservableCollection behavior for individual edits.</summary>
public sealed class BatchObservableCollection<T> : ObservableCollection<T>
{
    public bool ReplaceAll(IEnumerable<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        // Materialize before mutation: self-enumeration and failed enumeration
        // must not erase the previous display. Equality preserves item identity.
        T[] snapshot = values.ToArray();
        if (this.SequenceEqual(snapshot)) return false;
        CheckReentrancy();
        int previousCount = Count;
        Items.Clear();
        foreach (T value in snapshot) Items.Add(value);
        if (previousCount != Count) OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        return true;
    }
}
