using System.Collections.Specialized;
using System.ComponentModel;
using ACCcom.Core.Collections;
using Xunit;

namespace ACCcom.Core.Tests;

/// <summary>
/// WPF's ListCollectionView (used as FilteredRxEntries) throws
/// NotSupportedException for range events — an Add/Remove event with
/// NewItems.Count != 1. ObservableRangeCollection batches the underlying list
/// write but must still raise one single-item event per item; these tests pin
/// that contract so a future "simplification" can't reintroduce the crash.
/// </summary>
public class ObservableRangeCollectionTests
{
    private readonly List<NotifyCollectionChangedEventArgs> _events = new();
    private readonly List<string> _propertyChanges = new();

    private ObservableRangeCollection<int> Create()
    {
        var coll = new ObservableRangeCollection<int>();
        coll.CollectionChanged += (_, e) => _events.Add(e);
        ((System.ComponentModel.INotifyPropertyChanged)coll).PropertyChanged += (_, e) => _propertyChanges.Add(e.PropertyName!);
        return coll;
    }

    [Fact]
    public void AddRange_RaisesOneEventPerItem()
    {
        var coll = Create();

        coll.AddRange(new[] { 1, 2, 3 });

        Assert.Equal(3, _events.Count);
        Assert.All(_events, e =>
        {
            Assert.Equal(NotifyCollectionChangedAction.Add, e.Action);
            Assert.Single(e.NewItems!); // ListCollectionView contract
        });
        Assert.Equal(new[] { 1, 2, 3 }, coll);
    }

    [Fact]
    public void AddRange_FiresCountAndIndexerPropertyChangedOnceEach()
    {
        var coll = Create();

        coll.AddRange(new[] { 1, 2, 3, 4, 5 });

        Assert.Equal(1, _propertyChanges.Count(n => n == nameof(ObservableRangeCollection<int>.Count)));
        Assert.Equal(1, _propertyChanges.Count(n => n == "Item[]"));
    }

    [Fact]
    public void AddRange_EventsCarryIncrementingStartIndices()
    {
        var coll = Create();

        coll.AddRange(new[] { 1, 2, 3 });

        Assert.Equal(0, _events[0].NewStartingIndex);
        Assert.Equal(1, _events[1].NewStartingIndex);
        Assert.Equal(2, _events[2].NewStartingIndex);
    }

    [Fact]
    public void AddRange_AppendingToExistingItems_OffsetsStartIndex()
    {
        var coll = Create();
        coll.Add(10);
        _events.Clear(); // discard the Add(10) event; we only assert AddRange below

        coll.AddRange(new[] { 20, 30 });

        Assert.Equal(1, _events[0].NewStartingIndex);
        Assert.Equal(2, _events[1].NewStartingIndex);
    }

    [Fact]
    public void RemoveRange_RaisesOneEventPerRemovedItem()
    {
        var coll = Create();
        coll.AddRange(new[] { 1, 2, 3, 4 });

        _events.Clear();
        coll.RemoveRange(0, 2);

        Assert.Equal(2, _events.Count);
        Assert.All(_events, e =>
        {
            Assert.Equal(NotifyCollectionChangedAction.Remove, e.Action);
            Assert.Single(e.OldItems!); // ListCollectionView contract
        });
        Assert.Equal(new[] { 3, 4 }, coll);
    }

    [Fact]
    public void RemoveRange_EventsAllReportSameStartIndex()
    {
        var coll = Create();
        coll.AddRange(new[] { 1, 2, 3, 4 });

        _events.Clear();
        coll.RemoveRange(1, 2);

        // Removing [2,3] from the front: each single-item removal reports index 1,
        // matching how WPF's ListCollectionView observes front removals.
        Assert.All(_events, e => Assert.Equal(1, e.OldStartingIndex));
        Assert.Equal(new[] { 1, 4 }, coll);
    }

    [Fact]
    public void RemoveRange_FiresCountAndIndexerPropertyChangedOnceEach()
    {
        var coll = Create();
        coll.AddRange(new[] { 1, 2, 3, 4 });

        _propertyChanges.Clear();
        coll.RemoveRange(0, 2);

        Assert.Equal(1, _propertyChanges.Count(n => n == nameof(ObservableRangeCollection<int>.Count)));
        Assert.Equal(1, _propertyChanges.Count(n => n == "Item[]"));
    }

    [Fact]
    public void AddRange_Empty_NoEvents()
    {
        var coll = Create();

        coll.AddRange(Array.Empty<int>());

        Assert.Empty(_events);
        Assert.Empty(_propertyChanges);
        Assert.Empty(coll);
    }

    [Fact]
    public void RemoveRange_InvalidRange_NoOp()
    {
        var coll = Create();
        coll.AddRange(new[] { 1, 2, 3 });

        _events.Clear();
        coll.RemoveRange(5, 2); // beyond end

        Assert.Empty(_events);
        Assert.Equal(3, coll.Count);
    }

    [Fact]
    public void TrimTo_RemovesOldestInChunks_WithSingleItemEvents()
    {
        var coll = Create();
        coll.AddRange(Enumerable.Range(0, 10));

        _events.Clear();
        coll.TrimTo(6);

        Assert.Equal(4, _events.Count);
        Assert.All(_events, e => Assert.Equal(NotifyCollectionChangedAction.Remove, e.Action));
        Assert.Equal(Enumerable.Range(4, 6), coll);
    }
}