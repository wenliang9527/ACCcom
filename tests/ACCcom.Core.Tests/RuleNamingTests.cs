using System.Collections.Generic;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class RuleNamingTests
{
    [Fact]
    public void NextName_EmptySet_ReturnsRule1()
    {
        Assert.Equal("Rule_1", RuleNaming.NextName(new List<string>()));
    }

    [Fact]
    public void NextName_SkipsTakenNumbers()
    {
        var existing = new List<string> { "Rule_1", "Rule_2", "Rule_5" };
        Assert.Equal("Rule_3", RuleNaming.NextName(existing));
    }

    [Fact]
    public void NextName_ContinuesPastGap_WhenNumberedTaken()
    {
        // Rule_1 and Rule_2 are taken; 3 and 4 are free; 5 is taken too.
        var existing = new List<string> { "Rule_1", "Rule_2", "Rule_5" };
        Assert.Equal("Rule_3", RuleNaming.NextName(existing));
    }

    [Fact]
    public void NextName_IgnoresUnrelatedNames()
    {
        var existing = new List<string> { "Other", "Rule_1" };
        Assert.Equal("Rule_2", RuleNaming.NextName(existing));
    }
}