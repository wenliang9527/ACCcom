using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class PortMonitorServiceTests
{
    [Fact]
    public void Start_ThenStop_DoesNotThrow()
    {
        using var monitor = new PortMonitorService();
        monitor.Start(intervalMs: 50);
        monitor.Stop();
        monitor.Stop(); // double stop is safe
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var monitor = new PortMonitorService();
        monitor.Start(intervalMs: 50);
        monitor.Dispose();
        monitor.Dispose();
    }

    [Fact]
    public void Poll_InitialSnapshot_ReportsArrivedPorts()
    {
        using var monitor = new PortMonitorService();
        // Start captures the live OS snapshot; then clear the baseline so the
        // poll below compares against an empty set regardless of what ports
        // the test machine actually has.
        monitor.Start(intervalMs: 1000);
        monitor.Stop(); // no timer ticks; we drive Poll manually
        monitor.Poll([]);

        List<string>? arrived = null;
        List<string>? removed = null;
        monitor.PortsChanged += (a, r) => { arrived = a; removed = r; };

        monitor.Poll(["COM3", "COM4"]);

        Assert.NotNull(arrived);
        Assert.NotNull(removed);
        Assert.Contains("COM3", arrived);
        Assert.Contains("COM4", arrived);
        Assert.Empty(removed);
    }

    [Fact]
    public void Start_ReportsExistingPortsAsArrived()
    {
        // A monitor started while a device is already connected must surface it
        // immediately (arrived), not wait for the first timer tick. The startup
        // snapshot is injected so the test does not depend on which ports the
        // test machine actually has (same pattern as Poll(injectedPorts)).
        using var monitor = new PortMonitorService();

        List<string>? arrived = null;
        monitor.PortsChanged += (a, _) => arrived = a;

        monitor.Start(intervalMs: 1000, injectedPorts: new[] { "COM9" });
        monitor.Stop();

        Assert.NotNull(arrived);
        Assert.Contains("COM9", arrived);
    }

    [Fact]
    public void Poll_RemovedPort_ReportsRemoved()
    {
        using var monitor = new PortMonitorService();
        monitor.Start(intervalMs: 1000);
        monitor.Stop();
        monitor.Poll([]); // clear live-OS baseline

        // Establish a baseline with COM3 present.
        monitor.Poll(["COM3"]);

        List<string>? arrived = null;
        List<string>? removed = null;
        monitor.PortsChanged += (a, r) => { arrived = a; removed = r; };

        // COM3 disappears, nothing arrives.
        monitor.Poll([]);

        Assert.NotNull(arrived);
        Assert.NotNull(removed);
        Assert.Empty(arrived);
        Assert.Contains("COM3", removed);
    }

    [Fact]
    public void Poll_NoChange_DoesNotRaise()
    {
        using var monitor = new PortMonitorService();
        monitor.Start(intervalMs: 1000);
        monitor.Stop();
        monitor.Poll([]); // clear live-OS baseline
        monitor.Poll(["COM3"]);

        var raised = false;
        monitor.PortsChanged += (_, _) => raised = true;

        monitor.Poll(["COM3"]);

        Assert.False(raised);
    }

    [Fact]
    public void Poll_PortReinserted_ReportsArrivedAgain()
    {
        using var monitor = new PortMonitorService();
        monitor.Start(intervalMs: 1000);
        monitor.Stop();
        monitor.Poll([]); // clear live-OS baseline

        monitor.Poll(["COM3"]);
        monitor.Poll([]); // removed

        List<string>? arrived = null;
        monitor.PortsChanged += (a, _) => arrived = a;
        monitor.Poll(["COM3"]);

        Assert.NotNull(arrived);
        Assert.Contains("COM3", arrived);
    }

    [Fact]
    public void Poll_AfterDispose_DoesNotRaise()
    {
        var monitor = new PortMonitorService();
        monitor.Start(intervalMs: 1000);
        monitor.Stop();
        monitor.Poll(["COM3"]);

        var raised = false;
        monitor.PortsChanged += (_, _) => raised = true;

        monitor.Dispose();
        monitor.Poll(["COM4"]);

        Assert.False(raised);
    }

    [Fact]
    public void Poll_PortCaseInsensitive_NoFalseRemove()
    {
        using var monitor = new PortMonitorService();
        monitor.Start(intervalMs: 1000);
        monitor.Stop();
        monitor.Poll([]); // clear live-OS baseline

        monitor.Poll(["COM3"]);
        var raised = false;
        monitor.PortsChanged += (_, _) => raised = true;

        // "com3" differs only in case; must not count as remove+arrive.
        monitor.Poll(["com3"]);

        Assert.False(raised);
    }

    [Fact]
    public void Start_nonPositiveInterval_DoesNotThrow()
    {
        // System.Timers.Timer throws for non-positive intervals; the clamp must
        // make 0/negative behave as "as fast as possible" instead.
        using var monitor = new PortMonitorService();

        var exception = Record.Exception(() => monitor.Start(intervalMs: 0));
        Assert.Null(exception);
        monitor.Stop();

        var exception2 = Record.Exception(() => monitor.Start(intervalMs: -100));
        Assert.Null(exception2);
        monitor.Stop();
    }
}
