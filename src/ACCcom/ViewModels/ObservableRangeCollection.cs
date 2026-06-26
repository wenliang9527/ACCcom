using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace ACCcom.ViewModels;

public class ObservableRangeCollection<T> : ObservableCollection<T>
{
    public void RemoveRange(int index, int count)
    {
        if (count <= 0 || index < 0 || index + count > Count) return;
        if (index == 0 && count == Count)
        {
            Items.Clear();
        }
        else
        {
            for (int i = index + count - 1; i >= index; i--)
                Items.RemoveAt(i);
        }
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
