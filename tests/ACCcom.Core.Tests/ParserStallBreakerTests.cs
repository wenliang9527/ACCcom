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
        for (int i = 0; i < 3; i++)
        {
            // 100ms deadline vs a 5s sleeper: every call times out. The thread
            // keeps sleeping (cannot be aborted) — that is the leak being capped.
            Assert.Null(await manager.Engine.ExecuteAsync(data, DateTime.Now, 100));
        }

        Assert.Equal("stall", (await deactivated.Task.WaitAsync(TimeSpan.FromSeconds(5)))!);
        Assert.Null(manager.ActiveParserName);
        // Cleared engine: subsequent frames parse nothing, immediately.
        var sw = System.Diagnostics.Stopwatch.StartNew();
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
            Assert.Null(await engine.ExecuteAsync(new byte[] { 1 }, DateTime.Now, 100));

            engine.Load("return new List<ACCcom.Core.Models.FieldAnnotation>();");
            Assert.NotNull(await engine.ExecuteAsync(new byte[] { 1 }, DateTime.Now, 5000));
        }

        Assert.False(stalled, "alternating timeout/success must never trip the breaker");
    }
}
