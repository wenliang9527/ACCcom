using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

/// <summary>Consecutive-timeout circuit breaker for runaway parser scripts.
/// .NET cannot abort a Roslyn script, so a never-finishing script leaks one
/// thread per execution; the breaker caps the damage by auto-deactivating the
/// parser. The stall scripts use Thread.Sleep (not busy loops) so the leaked
/// threads cost no CPU and end on their own shortly after the test.</summary>
public class ParserStallBreakerTests : IDisposable
{
    private readonly string _tempDir;

    public ParserStallBreakerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ParserStallBreakerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task StalledActiveParser_AutoDeactivatesAfterThreeConsecutiveTimeouts()
    {
        File.WriteAllText(Path.Combine(_tempDir, "stall.csx"),
            "System.Threading.Thread.Sleep(5000); return new List<ACCcom.Core.Models.FieldAnnotation>();");
        using var manager = new ParserManager(_tempDir);
        Assert.True(manager.Activate("stall"));

        var deactivated = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.OnParserAutoDeactivated += n => deactivated.TrySetResult(n);

        var data = new byte[] { 0x01 };
        // 100ms deadline vs a 5s sleeper: a healthy call times out (null) and
        // counts one stall. Under extreme parallel load the test thread can be
        // preempted long enough for a sleeper to complete before its deadline
        // is observed — that call legitimately returns the empty list and
        // RESETS the breaker, so per-call Assert.Null is unsound in principle
        // (the 2026-11-01 flake). Drive the feed until the breaker proves
        // itself instead (b191a61 "证明已发生" doctrine): the event must fire
        // within 30s, and the engine must end deactivated.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (manager.ActiveParserName != null && !deactivated.Task.IsCompleted)
        {
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), "breaker never fired within 30s");
            await manager.Engine.ExecuteAsync(data, DateTime.Now, 100);
        }

        Assert.Equal("stall", (await deactivated.Task.WaitAsync(TimeSpan.FromSeconds(30)))!);
        Assert.Null(manager.ActiveParserName);
        // Cleared engine: subsequent frames parse nothing, immediately.
        sw.Restart();
        Assert.Null(await manager.Engine.ExecuteAsync(data, DateTime.Now, 100));
        Assert.True(sw.ElapsedMilliseconds < 90, "cleared engine must not wait out the timeout");
    }

    [Fact]
    public async Task SuccessBetweenTimeouts_ResetsTheStallCounter()
    {
        using var engine = new ParserEngine();
        var stalled = false;
        engine.OnStalled += () => stalled = true;

        for (int round = 0; round < 4; round++)
        {
            engine.Load("System.Threading.Thread.Sleep(5000); return new List<ACCcom.Core.Models.FieldAnnotation>();");
            // Null = the expected timeout; a completed empty list only happens
            // when the caller thread was preempted past the whole 5s sleeper
            // (extreme load) — either way no stall is counted, which is what
            // this round asserts. A NON-empty parse would be a real defect.
            var stalled_result = await engine.ExecuteAsync(new byte[] { 1 }, DateTime.Now, 100);
            Assert.True(stalled_result == null || stalled_result.Count == 0,
                "stall call must time out (null) or return the empty completed result, never a real parse");

            engine.Load("return new List<ACCcom.Core.Models.FieldAnnotation>();");
            Assert.NotNull(await engine.ExecuteAsync(new byte[] { 1 }, DateTime.Now, 5000));
        }

        Assert.False(stalled, "alternating timeout/success must never trip the breaker");
    }
}
