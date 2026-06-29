using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class ModbusPriorityQueue<T> where T : notnull
{
    private readonly int _maxLowPerSecond;
    private readonly Queue<T> _highQueue = new();
    private readonly Queue<T> _lowQueue = new();
    private readonly object _lock = new();
    private int _lowCountThisSecond;
    private DateTime _lastReset = DateTime.UtcNow;

    public ModbusPriorityQueue(int maxLowPerSecond = 50)
    {
        _maxLowPerSecond = maxLowPerSecond;
    }

    public bool TryEnqueue(T item, ModbusPriority priority)
    {
        lock (_lock)
        {
            if (priority == ModbusPriority.High)
            {
                _highQueue.Enqueue(item);
                return true;
            }
            ResetIfNeeded();
            if (_lowCountThisSecond >= _maxLowPerSecond) return false;
            _lowQueue.Enqueue(item);
            _lowCountThisSecond++;
            return true;
        }
    }

    public T TryDequeue()
    {
        lock (_lock)
        {
            if (_highQueue.Count > 0) return _highQueue.Dequeue();
            if (_lowQueue.Count > 0) return _lowQueue.Dequeue();
            throw new InvalidOperationException("Queue is empty");
        }
    }

    public int Count { get { lock (_lock) { return _highQueue.Count + _lowQueue.Count; } } }

    private void ResetIfNeeded()
    {
        if ((DateTime.UtcNow - _lastReset).TotalSeconds >= 1)
        {
            _lowCountThisSecond = 0;
            _lastReset = DateTime.UtcNow;
        }
    }
}
