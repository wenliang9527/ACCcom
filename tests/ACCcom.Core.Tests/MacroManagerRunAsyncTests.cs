using ACCcom.Core.Models;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class MacroManagerRunAsyncTests : IDisposable
{
    public void Dispose() { }

    [Fact]
    public void ConditionMet_Contains_RequiresLastResponse()
    {
        Assert.True(MacroManager.ConditionMet("contains:OK", "AT+OK"));
        Assert.False(MacroManager.ConditionMet("contains:OK", "ERR"));
        Assert.False(MacroManager.ConditionMet("contains:OK", null));
        Assert.True(MacroManager.ConditionMet("contains:ok", "AT+OK"), "match is case-insensitive");
    }

    [Fact]
    public void ConditionMet_NotContains_RequiresAbsenceOrNull()
    {
        Assert.True(MacroManager.ConditionMet("notcontains:ERR", "OK"));
        Assert.False(MacroManager.ConditionMet("notcontains:ERR", "ERR 42"));
        Assert.True(MacroManager.ConditionMet("notcontains:ERR", null));
    }

    [Fact]
    public void ConditionMet_UnknownFormat_NeverSkips()
    {
        Assert.True(MacroManager.ConditionMet("custom:thing", "whatever"));
    }

    [Fact]
    public async Task WaitFor_Matches_AdvancesWithoutTimeout()
    {
        using var manager = new MacroManager();
        var macro = new MacroTemplate
        {
            Name = "Wait",
            RepeatCount = 1,
            Steps = new List<MacroStep>
            {
                new() { Command = "ATE0", DelayMs = 0, WaitFor = "OK", WaitTimeoutMs = 30000 }
            }
        };

        var sent = new List<string>();
        var result = await manager.RunAsync(macro, (c, _) => sent.Add(c), s => s, _ => { },
            findResponse: _ => "ATE0\r\nOK");

        Assert.True(result);
        Assert.Single(sent);
    }

    [Fact]
    public async Task WaitFor_NoMatch_TimesOut_ThenContainsConditionSkipsNextStep()
    {
        using var manager = new MacroManager();
        var macro = new MacroTemplate
        {
            Name = "CondSkip",
            RepeatCount = 1,
            Steps = new List<MacroStep>
            {
                new() { Command = "AT+CMGR", DelayMs = 0, WaitFor = "OK", WaitTimeoutMs = 120 },
                new() { Command = "AT+DEL", DelayMs = 0, Condition = "contains:OK" }
            }
        };

        var sent = new List<string>();
        var result = await manager.RunAsync(macro, (c, _) => sent.Add(c), s => s, _ => { },
            findResponse: _ => "ERROR");

        Assert.True(result);
        // First step sent, second step skipped because "ERROR" lacks OK.
        Assert.Single(sent);
        Assert.Equal("AT+CMGR", sent[0]);
    }

    [Fact]
    public async Task WaitFor_Match_ThenConditionPasses_SendsNextStep()
    {
        using var manager = new MacroManager();
        var macro = new MacroTemplate
        {
            Name = "CondPass",
            RepeatCount = 1,
            Steps = new List<MacroStep>
            {
                new() { Command = "AT+CMGR", DelayMs = 0, WaitFor = "OK", WaitTimeoutMs = 30000 },
                new() { Command = "AT+DEL", DelayMs = 0, Condition = "contains:OK" }
            }
        };

        var sent = new List<string>();
        var result = await manager.RunAsync(macro, (c, _) => sent.Add(c), s => s, _ => { },
            findResponse: _ => "CMGR: 1\r\nOK");

        Assert.True(result);
        Assert.Equal(2, sent.Count);
    }

    [Fact]
    public async Task WaitFor_NoResponseSource_FallsBackToPause()
    {
        using var manager = new MacroManager();
        var macro = new MacroTemplate
        {
            Name = "Fallback",
            RepeatCount = 1,
            Steps = new List<MacroStep>
            {
                new() { Command = "AT", DelayMs = 0, WaitFor = "OK", WaitTimeoutMs = 3000 }
            }
        };

        var sent = new List<string>();
        var result = await manager.RunAsync(macro, (c, _) => sent.Add(c), s => s, _ => { });

        Assert.True(result);
        Assert.Single(sent);
    }

    [Fact]
    public async Task TestRunMacro()
    {
        using var manager = new MacroManager();
        var macro = new MacroTemplate
        {
            Name = "Basic",
            RepeatCount = 1,
            Steps = new List<MacroStep>
            {
                new() { Command = "AT", IsHex = false, DelayMs = 0 }
            }
        };

        var sent = new List<(string cmd, bool hex)>();
        var statuses = new List<string>();

        var result = await manager.RunAsync(macro, (c, h) => sent.Add((c, h)), s => s, s => statuses.Add(s));

        Assert.True(result);
        Assert.Single(sent);
        Assert.Equal("AT", sent[0].cmd);
        Assert.False(sent[0].hex);
        Assert.Contains(statuses, s => s.Contains("1/1"));
    }

    [Fact]
    public async Task TestRunMacroHex()
    {
        using var manager = new MacroManager();
        var macro = new MacroTemplate
        {
            Name = "Hex",
            RepeatCount = 1,
            Steps = new List<MacroStep>
            {
                new() { Command = "FF 01", IsHex = true, DelayMs = 0 }
            }
        };

        var sent = new List<(string cmd, bool hex)>();

        var result = await manager.RunAsync(macro, (c, h) => sent.Add((c, h)), s => s, _ => { });

        Assert.True(result);
        Assert.Single(sent);
        Assert.Equal("FF 01", sent[0].cmd);
        Assert.True(sent[0].hex);
    }

    [Fact]
    public async Task TestRunMacroRepeat()
    {
        using var manager = new MacroManager();
        var macro = new MacroTemplate
        {
            Name = "Repeat",
            RepeatCount = 3,
            Steps = new List<MacroStep>
            {
                new() { Command = "PING", IsHex = false, DelayMs = 0 }
            }
        };

        int sendCount = 0;

        var result = await manager.RunAsync(macro, (_, _) => Interlocked.Increment(ref sendCount), s => s, _ => { });

        Assert.True(result);
        Assert.Equal(3, sendCount);
    }

    [Fact]
    public async Task TestRunMacroStop()
    {
        using var manager = new MacroManager();
        var macro = new MacroTemplate
        {
            Name = "Stop",
            RepeatCount = 1000,
            Steps = new List<MacroStep>
            {
                new() { Command = "X", IsHex = false, DelayMs = 10 }
            }
        };

        var task = manager.RunAsync(macro, (_, _) => { }, s => s, _ => { });
        await Task.Delay(50);
        manager.Stop();
        var result = await task;

        Assert.False(result);
    }

    [Fact]
    public async Task TestExpandVariables()
    {
        using var manager = new MacroManager();
        var macro = new MacroTemplate
        {
            Name = "Vars",
            RepeatCount = 1,
            Steps = new List<MacroStep>
            {
                new() { Command = "AT+NAME", IsHex = false, DelayMs = 0 }
            }
        };

        var sent = new List<string>();
        var result = await manager.RunAsync(
            macro,
            (c, _) => sent.Add(c),
            s => s.Replace("AT+NAME", "AT+NAME=MyDevice"),
            _ => { });

        Assert.True(result);
        Assert.Equal("AT+NAME=MyDevice", sent[0]);
    }

    [Fact]
    public async Task TestRunMacroZeroRepeat()
    {
        using var manager = new MacroManager();
        var macro = new MacroTemplate
        {
            Name = "ZeroRepeat",
            RepeatCount = 0,
            RepeatDelayMs = 5,
            Steps = new List<MacroStep>
            {
                new() { Command = "LOOP", IsHex = false, DelayMs = 5 }
            }
        };

        int sendCount = 0;
        var task = manager.RunAsync(
            macro,
            (_, _) => Interlocked.Increment(ref sendCount),
            s => s,
            _ => { });

        await Task.Delay(100);
        manager.Stop();
        await task;

        Assert.True(sendCount > 1);
    }

    [Fact]
    public async Task TestIsRunningProperty()
    {
        using var manager = new MacroManager();
        Assert.False(manager.IsRunning);

        var macro = new MacroTemplate
        {
            Name = "Running",
            RepeatCount = 1000,
            Steps = new List<MacroStep>
            {
                new() { Command = "X", IsHex = false, DelayMs = 50 }
            }
        };

        var task = manager.RunAsync(macro, (_, _) => { }, s => s, _ => { });
        await Task.Delay(20);
        Assert.True(manager.IsRunning);

        manager.Stop();
        await task;
        Assert.False(manager.IsRunning);
    }
}
