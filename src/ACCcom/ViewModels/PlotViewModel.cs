using System.Collections.ObjectModel;

namespace ACCcom.ViewModels;

public class PlotViewModel : ObservableObject
{
    private readonly List<(DateTime Time, double Value)> _dataPoints = new();
    private readonly object _lock = new();
    private int _maxPoints = 200;

    public int MaxPoints
    {
        get => _maxPoints;
        set => SetField(ref _maxPoints, Math.Max(10, value));
    }

    private double _minValue;
    public double MinValue { get => _minValue; private set => SetField(ref _minValue, value); }

    private double _maxValue;
    public double MaxValue { get => _maxValue; private set => SetField(ref _maxValue, value); }

    private double _latestValue;
    public double LatestValue { get => _latestValue; private set => SetField(ref _latestValue, value); }

    private int _pointCount;
    public int PointCount { get => _pointCount; private set => SetField(ref _pointCount, value); }

    public event Action? DataChanged;

    public void AddPoint(double value)
    {
        lock (_lock)
        {
            _dataPoints.Add((DateTime.Now, value));
            while (_dataPoints.Count > _maxPoints)
                _dataPoints.RemoveAt(0);

            LatestValue = value;
            PointCount = _dataPoints.Count;

            if (_dataPoints.Count > 0)
            {
                MinValue = _dataPoints.Min(p => p.Value);
                MaxValue = _dataPoints.Max(p => p.Value);
            }
        }
        DataChanged?.Invoke();
    }

    public void Clear()
    {
        lock (_lock)
        {
            _dataPoints.Clear();
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
