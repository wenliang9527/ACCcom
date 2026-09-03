using ACCcom.Core.Collections;
using System.Collections.Generic;
using Xunit;

namespace ACCcom.Core.Tests;

public class MatchIndexNavigatorTests
{
    private static readonly List<string> Source = ["aaa", "match1", "bbb", "match2", "ccc", "match3"];

    private static bool IsMatch(string s) => s.StartsWith("match");

    [Fact]
    public void Step_EmptySource_ReturnsNull() =>
        Assert.Null(MatchIndexNavigator.Step(new List<string>(), IsMatch, null, true));

    [Fact]
    public void Step_NoMatches_ReturnsNull() =>
        Assert.Null(MatchIndexNavigator.Step(Source, _ => false, null, true));

    [Fact]
    public void Step_Forward_FromNoSelection_MovesToFirstMatch() =>
        Assert.Equal("match1", MatchIndexNavigator.Step(Source, IsMatch, null, true));

    [Fact]
    public void Step_Forward_FromFirstMatch_MovesToSecond()
    {
        var next = MatchIndexNavigator.Step(Source, IsMatch, "match1", true);
        Assert.Equal("match2", next);
    }

    [Fact]
    public void Step_Forward_AtLastMatch_ReturnsNull() =>
        Assert.Null(MatchIndexNavigator.Step(Source, IsMatch, "match3", true));

    [Fact]
    public void Step_Backward_FromSecondMatch_MovesToFirst() =>
        Assert.Equal("match1", MatchIndexNavigator.Step(Source, IsMatch, "match2", false));

    [Fact]
    public void Step_Backward_AtFirstMatch_ReturnsNull() =>
        Assert.Null(MatchIndexNavigator.Step(Source, IsMatch, "match1", false));

    [Fact]
    public void Step_CurrentNotAMatch_Forward_MovesToFirstMatch() =>
        Assert.Equal("match1", MatchIndexNavigator.Step(Source, IsMatch, "bbb", true));

    [Fact]
    public void Step_CurrentNotAMatch_Backward_MovesToFirstMatch() =>
        Assert.Equal("match1", MatchIndexNavigator.Step(Source, IsMatch, "bbb", false));
}