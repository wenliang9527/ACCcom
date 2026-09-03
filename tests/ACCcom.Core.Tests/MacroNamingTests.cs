using ACCcom.Core.Services;
using System.Collections.Generic;
using Xunit;

namespace ACCcom.Core.Tests;

public class MacroNamingTests
{
    [Fact]
    public void NextName_NoExisting_ReturnsPrefix1() =>
        Assert.Equal("Macro 1", MacroNaming.NextName(new List<string>()));

    [Fact]
    public void NextName_SkipsUsedNames() =>
        Assert.Equal("Macro 3", MacroNaming.NextName(["Macro 1", "Macro 2"]));

    [Fact]
    public void NextName_WithCustomPrefix() =>
        Assert.Equal("Init 1", MacroNaming.NextName(["Init 2", "Other"], "Init"));

    [Fact]
    public void NextName_DistinctNames_AreOrdinalCaseSensitive() =>
        Assert.Equal("Macro 1", MacroNaming.NextName(["macro 1", "MACRO 1"]));

    [Fact]
    public void NextName_NullExisting_Throws() =>
        Assert.Throws<System.ArgumentNullException>(() => MacroNaming.NextName(null!));
}