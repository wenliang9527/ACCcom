using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace ACCcom.ViewModels;

public class ObservableRangeCollection<T> : ObservableCollection<T>
{
    /// <summary>
    /// Adds a range of items and raises a single Add notification.
    /// Avoids one CollectionChanged event per item on high-frequency data.
    /// </summary>
    public void AddRange(IEnumerable<T> collection)
    {
        if (collection == null) throw new ArgumentNullException(nameof(collection));
        var list = collection as IList<T> ?? collection.ToList();
        if (list.Count == 0) return;

        foreach (var item in list)
            Items.Add(item);

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, list));
    }

    /// <summary>
    /// Removes a range of items and raises a single Remove notification
    /// (preserves scroll position in bound views, unlike a Reset).
    /// </summary>
    public void RemoveRange(int index, int count)
    {
        if (count <= 0 || index < 0 || index + count > Count) return;

        var removed = new T[count];
        for (int i = 0; i < count; i++)
            removed[i] = Items[index + i];

        // Fast path: single bulk shift instead of per-item RemoveAt moves.
        if (Items is List<T> list) list.RemoveRange(index, count);
        else for (int i = index + count - 1; i >= index; i--) Items.RemoveAt(i);

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, removed, index));
    }

    public void TrimTo(int maxSize)
    {
        if (Count <= maxSize) return;
        RemoveRange(0, Count - maxSize);
    }
}
