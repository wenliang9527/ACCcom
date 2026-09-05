using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace ACCcom.Core.Services;

/// <summary>
/// 性能指标收集器，线程安全的计数器和直方图。
/// 使用 MetricsCollector.Instance 单例访问。
/// 已知名称的计数器使用预分配字段 + Interlocked，避免接收热路径上的字典哈希与争用。
/// </summary>
public sealed class MetricsCollector
{
    private static readonly Lazy<MetricsCollector> _instance = new(() => new MetricsCollector());
    public static MetricsCollector Instance => _instance.Value;

    // 已知热路径计数器的预分配字段（每包/每次事件调用，避免 ConcurrentDictionary 开销）。
    private long _bytesReceived;
    private long _bytesSent;
    private long _portOpened;
    private long _portClosed;
    private long _errors;
    private long _parseSuccess;
    private long _parseFailure;
    private long _bufferOverruns;

    // 缓冲占用率：每包写入 volatile 字段，Prometheus 输出时结转，避免每包字典写入。
    private double _bufferUsage;

    private readonly ConcurrentDictionary<string, long> _counters = new();
    private readonly ConcurrentDictionary<string, double> _gauges = new();
    private readonly ConcurrentDictionary<string, Histogram> _histograms = new();
    private readonly long _startTimeTicks = Stopwatch.GetTimestamp();

    private MetricsCollector() { }

    // ── Counter 操作 ──

    public void IncrementCounter(string name, long value = 1)
    {
        switch (name)
        {
            case "acccom_serial_bytes_received_total": Interlocked.Add(ref _bytesReceived, value); return;
            case "acccom_serial_bytes_sent_total": Interlocked.Add(ref _bytesSent, value); return;
            case "acccom_serial_port_opened_total": Interlocked.Add(ref _portOpened, value); return;
            case "acccom_serial_port_closed_total": Interlocked.Add(ref _portClosed, value); return;
            case "acccom_serial_errors_total": Interlocked.Add(ref _errors, value); return;
            case "acccom_parser_parse_success_total": Interlocked.Add(ref _parseSuccess, value); return;
            case "acccom_parser_parse_failure_total": Interlocked.Add(ref _parseFailure, value); return;
            case "acccom_buffer_overrun_total": Interlocked.Add(ref _bufferOverruns, value); return;
        }
        _counters.AddOrUpdate(name, value, (_, old) => old + value);
    }

    public long GetCounter(string name)
    {
        switch (name)
        {
            case "acccom_serial_bytes_received_total": return Interlocked.Read(ref _bytesReceived);
            case "acccom_serial_bytes_sent_total": return Interlocked.Read(ref _bytesSent);
            case "acccom_serial_port_opened_total": return Interlocked.Read(ref _portOpened);
            case "acccom_serial_port_closed_total": return Interlocked.Read(ref _portClosed);
            case "acccom_serial_errors_total": return Interlocked.Read(ref _errors);
            case "acccom_parser_parse_success_total": return Interlocked.Read(ref _parseSuccess);
            case "acccom_parser_parse_failure_total": return Interlocked.Read(ref _parseFailure);
            case "acccom_buffer_overrun_total": return Interlocked.Read(ref _bufferOverruns);
        }
        return _counters.GetValueOrDefault(name, 0);
    }

    public void SetGauge(string name, double value)
    {
        _gauges[name] = value;
    }

    public double GetGauge(string name)
    {
        if (name == "acccom_buffer_usage_ratio") return Volatile.Read(ref _bufferUsage);
        return _gauges.GetValueOrDefault(name, 0);
    }

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
    public void SetBufferUsage(double ratio) => Volatile.Write(ref _bufferUsage, ratio);

    // ── Prometheus 格式输出 ──

    public string ToPrometheusFormat()
    {
        var sb = new StringBuilder();
        var uptime = Stopwatch.GetElapsedTime(_startTimeTicks);

        sb.AppendLine("# HELP acccom_uptime_seconds Application uptime in seconds");
        sb.AppendLine("# TYPE acccom_uptime_seconds gauge");
        sb.AppendLine($"acccom_uptime_seconds {uptime.TotalSeconds:F1}");

        AppendCounter(sb, "acccom_buffer_overrun_total", Interlocked.Read(ref _bufferOverruns));
        AppendCounter(sb, "acccom_parser_parse_failure_total", Interlocked.Read(ref _parseFailure));
        AppendCounter(sb, "acccom_parser_parse_success_total", Interlocked.Read(ref _parseSuccess));
        AppendCounter(sb, "acccom_serial_bytes_received_total", Interlocked.Read(ref _bytesReceived));
        AppendCounter(sb, "acccom_serial_bytes_sent_total", Interlocked.Read(ref _bytesSent));
        AppendCounter(sb, "acccom_serial_errors_total", Interlocked.Read(ref _errors));
        AppendCounter(sb, "acccom_serial_port_closed_total", Interlocked.Read(ref _portClosed));
        AppendCounter(sb, "acccom_serial_port_opened_total", Interlocked.Read(ref _portOpened));

        foreach (var kv in _counters.OrderBy(x => x.Key))
        {
            AppendCounter(sb, kv.Key, kv.Value);
        }

        sb.AppendLine("# TYPE acccom_buffer_usage_ratio gauge");
        sb.AppendLine($"acccom_buffer_usage_ratio {Volatile.Read(ref _bufferUsage):F4}");

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

    private static void AppendCounter(StringBuilder sb, string name, long value)
    {
        sb.AppendLine($"# TYPE {name} counter");
        sb.AppendLine($"{name} {value}");
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
    private readonly object _sumLock = new();

    public long Count => Interlocked.Read(ref _count);
    public double Sum
    {
        get { lock (_sumLock) return _sum; }
    }

    public void Record(double value)
    {
        // Bucket counts use Interlocked so the per-frame parse path never takes
        // a lock; Sum is a rare/read-only metric and keeps its own small lock.
        int idx = Array.BinarySearch(BucketBounds, value);
        if (idx < 0) idx = ~idx;
        else idx++;
        for (int i = idx; i < _bucketCounts.Length; i++)
            Interlocked.Increment(ref _bucketCounts[i]);
        Interlocked.Increment(ref _count);
        lock (_sumLock) _sum += value;
    }

    public IReadOnlyList<BucketEntry> GetBuckets()
    {
        var result = new List<BucketEntry>(BucketBounds.Length + 1);
        // Snapshot under no lock: each bucket is read via Volatile.Read, and the
        // per-bucket ordering is not a strict invariant consumers rely on.
        for (int i = 0; i < BucketBounds.Length; i++)
            result.Add(new BucketEntry(BucketBounds[i], Volatile.Read(ref _bucketCounts[i])));
        result.Add(new BucketEntry(double.PositiveInfinity, Volatile.Read(ref _bucketCounts[^1])));
        return result;
    }
}

public record BucketEntry(double Le, long Count);
