using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace ACCcom.Core.Services;

/// <summary>
/// 性能监控指标采集服务，使用 DiagnosticListener 暴露性能事件。
/// 提供 MetricsCollector 单例供各服务注入，支持 Prometheus 格式输出。
/// </summary>
public sealed class MetricsService : IDisposable
{
    public const string SourceName = "ACCcom.Metrics";
    private static readonly Lazy<MetricsService> _instance = new(() => new MetricsService());
    public static MetricsService Instance => _instance.Value;

    public DiagnosticListener Listener { get; } = new(SourceName);

    private MetricsService() { }

    public void Dispose() => Listener.Dispose();
}

/// <summary>
/// 性能指标收集器，线程安全的计数器和直方图。
/// 使用 MetricsCollector.Instance 单例访问。
/// </summary>
public sealed class MetricsCollector
{
    private static readonly Lazy<MetricsCollector> _instance = new(() => new MetricsCollector());
    public static MetricsCollector Instance => _instance.Value;

    private readonly ConcurrentDictionary<string, long> _counters = new();
    private readonly ConcurrentDictionary<string, double> _gauges = new();
    private readonly ConcurrentDictionary<string, Histogram> _histograms = new();
    private readonly long _startTimeTicks = Stopwatch.GetTimestamp();

    private MetricsCollector() { }

    // ── Counter 操作 ──

    public void IncrementCounter(string name, long value = 1)
    {
        _counters.AddOrUpdate(name, value, (_, old) => old + value);
        EmitEvent(name, value);
    }

    public long GetCounter(string name) => _counters.GetValueOrDefault(name, 0);

    // ── Gauge 操作 ──

    public void SetGauge(string name, double value)
    {
        _gauges[name] = value;
    }

    public double GetGauge(string name) => _gauges.GetValueOrDefault(name, 0);

    // ── Histogram 操作 ──

    public void RecordHistogram(string name, double value)
    {
        var histogram = _histograms.GetOrAdd(name, _ => new Histogram());
        histogram.Record(value);
    }

    // ── 便捷方法 ──

    public void RecordBytesReceived(long bytes) => IncrementCounter("acccom_serial_bytes_received_total", bytes);
    public void RecordBytesSent(long bytes) => IncrementCounter("acccom_serial_bytes_sent_total", bytes);
    public void RecordPortOpened() => IncrementCounter("acccom_serial_port_opened_total");
    public void RecordPortClosed() => IncrementCounter("acccom_serial_port_closed_total");
    public void RecordError() => IncrementCounter("acccom_serial_errors_total");
    public void RecordParseCompleted(bool success, double elapsedMs)
    {
        IncrementCounter(success ? "acccom_parser_parse_success_total" : "acccom_parser_parse_failure_total");
        RecordHistogram("acccom_parser_parse_duration_ms", elapsedMs);
    }
    public void RecordBufferOverrun() => IncrementCounter("acccom_buffer_overrun_total");
    public void SetBufferUsage(double ratio) => SetGauge("acccom_buffer_usage_ratio", ratio);

    // ── DiagnosticSource 事件发射 ──

    private void EmitEvent(string name, object value)
    {
        var listener = MetricsService.Instance.Listener;
        if (listener.IsEnabled())
        {
            listener.Write(name, new { Value = value, Timestamp = DateTime.UtcNow });
        }
    }

    // ── Prometheus 格式输出 ──

    public string ToPrometheusFormat()
    {
        var sb = new StringBuilder();
        var uptime = Stopwatch.GetElapsedTime(_startTimeTicks);

        sb.AppendLine("# HELP acccom_uptime_seconds Application uptime in seconds");
        sb.AppendLine("# TYPE acccom_uptime_seconds gauge");
        sb.AppendLine($"acccom_uptime_seconds {uptime.TotalSeconds:F1}");

        foreach (var kv in _counters.OrderBy(x => x.Key))
        {
            sb.AppendLine($"# TYPE {kv.Key} counter");
            sb.AppendLine($"{kv.Key} {kv.Value}");
        }

        foreach (var kv in _gauges.OrderBy(x => x.Key))
        {
            sb.AppendLine($"# TYPE {kv.Key} gauge");
            sb.AppendLine($"{kv.Key} {kv.Value:F4}");
        }

        foreach (var kv in _histograms.OrderBy(x => x.Key))
        {
            var h = kv.Value;
            sb.AppendLine($"# TYPE {kv.Key} histogram");
            foreach (var bucket in h.GetBuckets())
            {
                sb.AppendLine($"{kv.Key}_bucket{{le=\"{bucket.Le}\"}} {bucket.Count}");
            }
            sb.AppendLine($"{kv.Key}_sum {h.Sum:F4}");
            sb.AppendLine($"{kv.Key}_count {h.Count}");
        }

        return sb.ToString();
    }
}

/// <summary>
/// 简易直方图实现，支持 Prometheus 风格的桶计数。
/// </summary>
public sealed class Histogram
{
    private static readonly double[] BucketBounds = [1, 5, 10, 25, 50, 100, 250, 500, 1000, 5000];
    private long[] _bucketCounts = new long[BucketBounds.Length + 1];
    private long _count;
    private double _sum;
    private readonly object _lock = new();

    public long Count => Interlocked.Read(ref _count);
    public double Sum
    {
        get { lock (_lock) return _sum; }
    }

    public void Record(double value)
    {
        lock (_lock)
        {
            _sum += value;
            _count++;

            int idx = Array.BinarySearch(BucketBounds, value);
            if (idx < 0) idx = ~idx;
            else idx++;
            for (int i = idx; i < _bucketCounts.Length; i++)
                _bucketCounts[i]++;
        }
    }

    public IReadOnlyList<BucketEntry> GetBuckets()
    {
        var result = new List<BucketEntry>(BucketBounds.Length + 1);
        lock (_lock)
        {
            for (int i = 0; i < BucketBounds.Length; i++)
                result.Add(new BucketEntry(BucketBounds[i], Volatile.Read(ref _bucketCounts[i])));
            result.Add(new BucketEntry(double.PositiveInfinity, Volatile.Read(ref _bucketCounts[^1])));
        }
        return result;
    }
}

public record BucketEntry(double Le, long Count);
