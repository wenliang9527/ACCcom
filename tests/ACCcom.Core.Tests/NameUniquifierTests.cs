using System.Collections.Generic;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class NameUniquifierTests
{
    [Fact]
    public void UniqueName_UnusedBaseName_IsReturnedAsIs()
    {
        Assert.Equal("MyPage", NameUniquifier.UniqueName(new[] { "Other", "Page" }, "MyPage"));
    }

    [Fact]
    public void UniqueName_TakenBaseName_AppendsNumberedSuffix()
    {
        Assert.Equal("MyPage (2)", NameUniquifier.UniqueName(new[] { "MyPage" }, "MyPage"));
    }

    [Fact]
    public void UniqueName_SkipsTakenSuffixes()
    {
        var existing = new[] { "MyPage", "MyPage (2)", "MyPage (3)" };
        Assert.Equal("MyPage (4)", NameUniquifier.UniqueName(existing, "MyPage"));
    }

    [Fact]
    public void UniqueName_CaseSensitiveOrdinal()
    {
        // "mypage" is a different name under ordinal comparison.
        Assert.Equal("MyPage", NameUniquifier.UniqueName(new[] { "mypage" }, "MyPage"));
    }

    [Fact]
    public void UniqueName_EmptyBaseName_ReturnsEmpty()
    {
        Assert.Equal("", NameUniquifier.UniqueName(new[] { "" }, ""));
    }

    [Fact]
    public void UniqueName_EmptyExistingNames_ReturnsBaseName()
    {
        Assert.Equal("MyPage", NameUniquifier.UniqueName(new List<string>(), "MyPage"));
    }
}