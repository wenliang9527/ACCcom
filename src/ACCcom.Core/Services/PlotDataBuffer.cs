namespace ACCcom.Core.Services;

/// <summary>
/// Ring-buffer data store for plot series with incrementally maintained min/max.
/// Extracted from the WPF PlotViewModel so the extrema logic is unit-testable
/// without a UI host. The ViewModel owns the ring; this class owns the math.
/// </summary>
public sealed class PlotDataBuffer
{
    private readonly List<double> _values = new();
    private int _maxPoints;
    private double _minValue;
    private double _maxValue;

    public PlotDataBuffer(int maxPoints = 200)
    {
        _maxPoints = Math.Max(10, maxPoints);
    }

    public int MaxPoints
    {
        get => _maxPoints;
        set
        {
            var clamped = Math.Max(10, value);
            if (clamped == _maxPoints) return;
            _maxPoints = clamped;
            TrimToCapacity();
        }
    }

    public int Count => _values.Count;
    public double MinValue => _minValue;
    public double MaxValue => _maxValue;

    /// <summary>Adds a value, trimming the oldest once the capacity is exceeded,
    /// and maintaining min/max incrementally (full rescan only when a point was
    /// evicted, since the evicted point might have been an extremum).</summary>
    public void Add(double value)
    {
        // NaN can never be a valid plot point: it would poison the incremental
        // min/max (NaN < x is always false) and render as garbage on the plot.
        // Drop it, same as Histogram.Record.
        if (double.IsNaN(value)) return;

        _values.Add(value);

        if (_values.Count > _maxPoints)
        {
            _values.RemoveAt(0);
            RecomputeMinMax();
        }
        else if (_values.Count == 1)
        {
            _minValue = value;
            _maxValue = value;
        }
        else
        {
            if (value < _minValue) _minValue = value;
            if (value > _maxValue) _maxValue = value;
        }
    }

    public void Clear()
    {
        _values.Clear();
        _minValue = 0;
        _maxValue = 0;
    }

    public IReadOnlyList<double> GetSnapshot() => new List<double>(_values);

    private void TrimToCapacity()
    {
        if (_values.Count <= _maxPoints) return;
        _values.RemoveRange(0, _values.Count - _maxPoints);
        RecomputeMinMax();
    }

    private void RecomputeMinMax()
    {
        double min = double.MaxValue, max = double.MinValue;
        foreach (var v in _values)
        {
            if (v < min) min = v;
            if (v > max) max = v;
        }
        _minValue = min;
        _maxValue = max;
    }
}
