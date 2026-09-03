using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace ACCcom.Core.Collections;

/// <summary>
/// An <see cref="ObservableCollection{T}"/> that batches the *underlying* writes
/// (one shift of the internal list instead of N per-item moves) while still
/// raising one <see cref="NotifyCollectionChangedAction.Add"/>/<see cref="NotifyCollectionChangedAction.Remove"/>
/// event **per item**. WPF's <see cref="System.Windows.Data.ListCollectionView"/>
/// rejects range events (<c>NewItems.Count != 1</c> throws
/// <c>NotSupportedException(RangeActionsNotSupported)</c>), so batching the
/// event is not an option — batching only the list write is.
/// </summary>
public class ObservableRangeCollection<T> : ObservableCollection<T>
{
    /// <summary>Adds a range. The internal list is extended in one bulk pass
    /// (<see cref="List{T}.AddRange"/> instead of N per-item adds, so there is a
    /// single capacity check/grow and one memcpy); the PropertyChanged
    /// notifications (Count/Item[]) fire once; the CollectionChanged events fire
    /// one per item so ListCollectionView can process them individually.</summary>
    public void AddRange(IEnumerable<T> collection)
    {
        if (collection == null) throw new ArgumentNullException(nameof(collection));
        var list = collection as IList<T> ?? collection.ToList();
        if (list.Count == 0) return;

        var startIndex = Items.Count;
        if (Items is List<T> inner)
            inner.AddRange(list);
        else
            foreach (var item in list)
                Items.Add(item);

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));

        for (int i = 0; i < list.Count; i++)
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Add, list[i], startIndex + i));
    }

    /// <summary>Removes a range. The internal list is shifted in one pass; the
    /// events fire one per removed item (all at the same start index, matching
    /// how WPF removes items one at a time from the front).</summary>
    public void RemoveRange(int index, int count)
    {
        if (count <= 0 || index < 0 || index + count > Count) return;

        var removed = new T[count];
        for (int i = 0; i < count; i++)
            removed[i] = Items[index + i];

        if (Items is List<T> list) list.RemoveRange(index, count);
        else for (int i = index + count - 1; i >= index; i--) Items.RemoveAt(i);

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));

        for (int i = 0; i < count; i++)
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Remove, removed[i], index));
    }

    public void TrimTo(int maxSize)
    {
        if (Count <= maxSize) return;
        RemoveRange(0, Count - maxSize);
    }
}