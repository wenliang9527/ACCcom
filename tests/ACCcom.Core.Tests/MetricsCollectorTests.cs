using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

/// <summary>
/// MetricsCollector 是进程级单例，其他测试类（DataBufferService、SerialService
/// 等）会在并行执行时间接递增同一计数器。因此对已知计数器断言用「增量下限」：
/// 若路由回归（如又退回字典），字段读数增量将为 0，断言失败；并发多增只会使
/// 增量更大，不影响通过。唯一命名的动态计数器则可用精确相等断言。
/// </summary>
public class MetricsCollectorTests
{
    private static readonly MetricsCollector Metrics = MetricsCollector.Instance;

    private static long ReadDelta(string name, Action record)
    {
        long before = Metrics.GetCounter(name);
        record();
        return Metrics.GetCounter(name) - before;
    }

    [Fact]
    public void KnownCounters_RouteToPreallocatedFields()
    {
        Assert.True(ReadDelta("acccom_serial_bytes_received_total", () => Metrics.RecordBytesReceived(5)) >= 5);
        Assert.True(ReadDelta("acccom_serial_bytes_sent_total", () => Metrics.RecordBytesSent(7)) >= 7);
        Assert.True(ReadDelta("acccom_serial_port_opened_total", Metrics.RecordPortOpened) >= 1);
        Assert.True(ReadDelta("acccom_serial_port_closed_total", Metrics.RecordPortClosed) >= 1);
        Assert.True(ReadDelta("acccom_serial_errors_total", Metrics.RecordError) >= 1);
        Assert.True(ReadDelta("acccom_buffer_overrun_total", Metrics.RecordBufferOverrun) >= 1);
        Assert.True(ReadDelta("acccom_parser_parse_success_total", () => Metrics.RecordParseCompleted(true, 1)) >= 1);
        Assert.True(ReadDelta("acccom_parser_parse_failure_total", () => Metrics.RecordParseCompleted(false, 1)) >= 1);
    }

    [Fact]
    public void IncrementCounter_KnownName_TargetsSameField()
    {
        Assert.True(ReadDelta("acccom_serial_errors_total", () => Metrics.IncrementCounter("acccom_serial_errors_total", 3)) >= 3);
    }

    [Fact]
    public void IncrementCounter_UnknownName_StoresInDictionary()
    {
        const string name = "acccom_test_dynamic_counter";
        Metrics.IncrementCounter(name, 2);
        Metrics.IncrementCounter(name, 3);
        Assert.Equal(5, Metrics.GetCounter(name));
    }

    [Fact]
    public void SetGauge_UnknownName_ReadsBackExact()
    {
        const string name = "acccom_test_dynamic_gauge";
        Metrics.SetGauge(name, 1.5);
        Assert.Equal(1.5, Metrics.GetGauge(name));
    }

    [Fact]
    public void GetGauge_UnknownName_ReturnsZero()
    {
        Assert.Equal(0, Metrics.GetGauge("acccom_test_unknown_gauge"));
    }

    [Fact]
    public void SetBufferUsage_IsEmittedInPrometheusOutput()
    {
        Metrics.SetBufferUsage(0.5);
        var output = Metrics.ToPrometheusFormat();
        Assert.Contains("# TYPE acccom_buffer_usage_ratio gauge", output);
        Assert.Contains("acccom_buffer_usage_ratio ", output);
    }

    [Fact]
    public async Task ParallelIncrements_LoseNoUpdates()
    {
        const string name = "acccom_serial_errors_total";
        const int threads = 8;
        const int perThread = 5000;

        long before = Metrics.GetCounter(name);
        var barrier = new Barrier(threads);
        var tasks = Enumerable.Range(0, threads).Select(_ => Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (int i = 0; i < perThread; i++) Metrics.RecordError();
        })).ToArray();
        await Task.WhenAll(tasks);

        // 并发下只证明本批增量的下限（Interlocked 不会丢更新；其他测试可能多增）。
        Assert.True(Metrics.GetCounter(name) - before >= threads * perThread);
    }

    [Fact]
    public void ToPrometheusFormat_IncludesKnownCounters()
    {
        Metrics.RecordBytesReceived(1);
        var output = Metrics.ToPrometheusFormat();

        Assert.Contains("# TYPE acccom_serial_bytes_received_total counter", output);
        Assert.Contains("acccom_serial_bytes_received_total ", output);
        Assert.Contains("# TYPE acccom_buffer_usage_ratio gauge", output);
        Assert.Contains("acccom_buffer_usage_ratio ", output);
    }
}
