using ACCcom.Core.Models;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class TriggerServiceTests : IDisposable
{
    private readonly TriggerService _sut = new();
    private readonly string _tempFile = Path.Combine(Path.GetTempPath(), $"triggers_{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_tempFile)) File.Delete(_tempFile);
    }

    private static LogEntry MakeEntry(string text = "hello world", string hex = "48 45 4C 4C 4F", string direction = "RX")
    {
        return new LogEntry { Id = 1, Timestamp = DateTime.Now, Direction = direction, PortTag = "COM1", RawHex = hex, Text = text };
    }

    [Fact]
    public void TestRegexMatch()
    {
        var rule = new TriggerRule { Name = "r1", Pattern = "^hello.*world$", MatchMode = "regex", Enabled = true };
        _sut.AddRule(rule);

        TriggerRule? fired = null;
        _sut.OnTriggerFired += (r, _) => fired = r;

        _sut.Evaluate(MakeEntry());

        Assert.Equal("r1", fired!.Name);
    }

    [Fact]
    public void TestContainsMatch()
    {
        var rule = new TriggerRule { Name = "c1", Pattern = "ell", MatchMode = "contains", Enabled = true };
        _sut.AddRule(rule);

        TriggerRule? fired = null;
        _sut.OnTriggerFired += (r, _) => fired = r;

        _sut.Evaluate(MakeEntry());

        Assert.Equal("c1", fired!.Name);
    }

    [Fact]
    public void TestExactMatch()
    {
        var rule = new TriggerRule { Name = "e1", Pattern = "hello world", MatchMode = "exact", Enabled = true };
        _sut.AddRule(rule);

        TriggerRule? fired = null;
        _sut.OnTriggerFired += (r, _) => fired = r;

        _sut.Evaluate(MakeEntry());

        Assert.Equal("e1", fired!.Name);
    }

    [Fact]
    public void TestNoMatch()
    {
        var rule = new TriggerRule { Name = "n1", Pattern = "xyz", MatchMode = "contains", Enabled = true };
        _sut.AddRule(rule);

        bool fired = false;
        _sut.OnTriggerFired += (_, _) => fired = true;

        _sut.Evaluate(MakeEntry());

        Assert.False(fired);
    }

    [Fact]
    public void TestDisabledRuleNotFired()
    {
        var rule = new TriggerRule { Name = "d1", Pattern = "hello", MatchMode = "contains", Enabled = false };
        _sut.AddRule(rule);

        bool fired = false;
        _sut.OnTriggerFired += (_, _) => fired = true;

        _sut.Evaluate(MakeEntry());

        Assert.False(fired);
    }

    [Fact]
    public void TestMultipleRules()
    {
        _sut.AddRule(new TriggerRule { Name = "r1", Pattern = "hello", MatchMode = "contains", Enabled = true });
        _sut.AddRule(new TriggerRule { Name = "r2", Pattern = "^hello", MatchMode = "regex", Enabled = true });
        _sut.AddRule(new TriggerRule { Name = "r3", Pattern = "xyz", MatchMode = "contains", Enabled = true });

        var fired = new List<string>();
        _sut.OnTriggerFired += (r, _) => fired.Add(r.Name);

        _sut.Evaluate(MakeEntry());

        Assert.Equal(2, fired.Count);
        Assert.Contains("r1", fired);
        Assert.Contains("r2", fired);
    }

    [Fact]
    public void TestDirectionFilter()
    {
        var rule = new TriggerRule { Name = "tx1", Pattern = "hello", MatchMode = "contains", Direction = "TX", Enabled = true };
        _sut.AddRule(rule);

        bool fired = false;
        _sut.OnTriggerFired += (_, _) => fired = true;

        _sut.Evaluate(MakeEntry(direction: "RX"));

        Assert.False(fired);

        _sut.Evaluate(MakeEntry(direction: "TX"));

        Assert.True(fired);
    }

    [Fact]
    public void TestHexMatch()
    {
        var rule = new TriggerRule { Name = "hex1", Pattern = "AB", MatchMode = "contains", MatchHex = true, Enabled = true };
        _sut.AddRule(rule);

        TriggerRule? fired = null;
        _sut.OnTriggerFired += (r, _) => fired = r;

        _sut.Evaluate(MakeEntry(hex: "AB 12 CD"));

        Assert.Equal("hex1", fired!.Name);
    }

    [Fact]
    public void TestAddRemoveRule()
    {
        _sut.AddRule(new TriggerRule { Name = "x1", Pattern = "a", MatchMode = "contains", Enabled = true });
        Assert.Single(_sut.Rules);

        _sut.RemoveRule("x1");
        Assert.Empty(_sut.Rules);
    }

    [Fact]
    public void TestRemoveRuleByNameOnly()
    {
        _sut.AddRule(new TriggerRule { Name = "a", Pattern = "a", Enabled = true });
        _sut.AddRule(new TriggerRule { Name = "b", Pattern = "b", Enabled = true });

        _sut.RemoveRule("a");

        Assert.Single(_sut.Rules);
        Assert.Equal("b", _sut.Rules[0].Name);
    }

    [Fact]
    public void TestLoadSaveRules()
    {
        var rules = new List<TriggerRule>
        {
            new() { Name = "s1", Pattern = "pat1", MatchMode = "regex", Enabled = true },
            new() { Name = "s2", Pattern = "pat2", MatchMode = "exact", Enabled = false }
        };

        TriggerService.SaveRules(rules, _tempFile);
        var loaded = TriggerService.LoadRules(_tempFile);

        Assert.Equal(2, loaded.Count);
        Assert.Equal("s1", loaded[0].Name);
        Assert.Equal("regex", loaded[0].MatchMode);
        Assert.Equal("s2", loaded[1].Name);
        Assert.False(loaded[1].Enabled);
    }

    [Fact]
    public void TestLoadRules_FileNotExist_ReturnsEmpty()
    {
        var loaded = TriggerService.LoadRules(Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid()}.json"));
        Assert.Empty(loaded);
    }

    [Fact]
    public void TestRegexBadPattern_ReturnsFalse()
    {
        var rule = new TriggerRule { Name = "bad", Pattern = "[invalid", MatchMode = "regex", Enabled = true };
        _sut.AddRule(rule);

        bool fired = false;
        _sut.OnTriggerFired += (_, _) => fired = true;

        _sut.Evaluate(MakeEntry());

        Assert.False(fired);
    }
}
