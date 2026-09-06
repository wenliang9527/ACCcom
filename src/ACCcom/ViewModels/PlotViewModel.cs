using System.Collections.ObjectModel;
using ACCcom.Core.Services;

namespace ACCcom.ViewModels;

public class PlotViewModel : ObservableObject
{
    private readonly PlotDataBuffer _buffer;
    private readonly List<(DateTime Time, double Value)> _dataPoints = new();
    private readonly object _lock = new();

    public int MaxPoints
    {
        get => _buffer.MaxPoints;
        set => SetField(ref _maxPoints, _buffer.MaxPoints = value);
    }

    private int _maxPoints;

    private double _minValue;
    public double MinValue { get => _minValue; private set => SetField(ref _minValue, value); }

    private double _maxValue;
    public double MaxValue { get => _maxValue; private set => SetField(ref _maxValue, value); }

    private double _latestValue;
    public double LatestValue { get => _latestValue; private set => SetField(ref _latestValue, value); }

    private int _pointCount;
    public int PointCount { get => _pointCount; private set => SetField(ref _pointCount, value); }

    public event Action? DataChanged;

    public PlotViewModel(int maxPoints = 200)
    {
        _buffer = new PlotDataBuffer(maxPoints);
        _maxPoints = _buffer.MaxPoints;
    }

    public void AddPoint(double value)
    {
        lock (_lock)
        {
            _dataPoints.Add((DateTime.Now, value));
            _buffer.Add(value);

            // Keep the UI snapshot list trimmed to the same capacity as the buffer.
            if (_dataPoints.Count > _buffer.MaxPoints)
                _dataPoints.RemoveRange(0, _dataPoints.Count - _buffer.MaxPoints);

            MinValue = _buffer.MinValue;
            MaxValue = _buffer.MaxValue;
            LatestValue = value;
            PointCount = _dataPoints.Count;
        }
        DataChanged?.Invoke();
    }

    public void Clear()
    {
        lock (_lock)
        {
            _dataPoints.Clear();
            _buffer.Clear();
            MinValue = 0;
            MaxValue = 0;
            LatestValue = 0;
            PointCount = 0;
        }
        DataChanged?.Invoke();
    }

    public List<(DateTime Time, double Value)> GetSnapshot()
    {
        lock (_lock)
        {
            return new List<(DateTime, double)>(_dataPoints);
        }
    }
}
