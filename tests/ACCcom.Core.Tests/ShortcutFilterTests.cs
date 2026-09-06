using ACCcom.Core.Models;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class ShortcutFilterTests
{
    private static ShortcutItem Item(string name) => new() { Name = name, Command = "AA" };

    [Fact]
    public void IsMatch_EmptyFilter_MatchesEverything()
    {
        Assert.True(ShortcutFilter.IsMatch(Item("Send"), ""));
        Assert.True(ShortcutFilter.IsMatch(Item("Send"), null));
        Assert.True(ShortcutFilter.IsMatch(Item("Send"), "   "));
    }

    [Fact]
    public void IsMatch_Substring_CaseInsensitive()
    {
        Assert.True(ShortcutFilter.IsMatch(Item("Send String"), "send"));
        Assert.True(ShortcutFilter.IsMatch(Item("send string"), "STRING"));
        Assert.True(ShortcutFilter.IsMatch(Item("HELLO"), "ell"));
    }

    [Fact]
    public void IsMatch_NonMatching_ReturnsFalse()
    {
        Assert.False(ShortcutFilter.IsMatch(Item("Send"), "receive"));
        Assert.False(ShortcutFilter.IsMatch(Item("Send"), "zzz"));
    }
}