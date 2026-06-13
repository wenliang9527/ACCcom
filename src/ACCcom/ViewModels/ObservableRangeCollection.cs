using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace ACCcom.ViewModels;

public class ObservableRangeCollection<T> : ObservableCollection<T>
{
    public void RemoveRange(int index, int count)
    {
        if (count <= 0 || index < 0 || index + count > Count) return;
        for (int i = count - 1; i >= 0; i--)
            Items.RemoveAt(index + i);
        OnPropertyChanged(new PropertyChangedEventArgs("Count"));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public void TrimTo(int maxSize)
    {
        if (Count <= maxSize) return;
        RemoveRange(0, Count - maxSize);
    }
}
