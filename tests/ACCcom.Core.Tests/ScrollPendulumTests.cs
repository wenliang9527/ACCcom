using ACCcom.Core.Collections;
using Xunit;

namespace ACCcom.Core.Tests;

public class ScrollPendulumTests
{
    [Fact]
    public void AtBottom_ReturnsTrue()
    {
        // offset 500 + viewport 500 == extent 1000 → pinned
        Assert.True(ScrollPendulum.ShouldAutoScroll(500, 500, 1000));
    }

    [Fact]
    public void WithinToleranceOfBottom_ReturnsTrue()
    {
        // 3px above the true bottom is within the 8px tolerance
        Assert.True(ScrollPendulum.ShouldAutoScroll(497, 500, 1000));
    }

    [Fact]
    public void ScrolledUp_ReturnsFalse()
    {
        // User scrolled 200px up; new data must not yank them back down
        Assert.False(ScrollPendulum.ShouldAutoScroll(300, 500, 1000));
    }

    [Fact]
    public void EmptyViewport_ReturnsTrue()
    {
        // No layout yet → follow (the first entries should bring the view down)
        Assert.True(ScrollPendulum.ShouldAutoScroll(0, 0, 0));
    }

    [Fact]
    public void NearTop_WithLargeExtent_ReturnsFalse()
    {
        Assert.False(ScrollPendulum.ShouldAutoScroll(0, 500, 10000));
    }

    [Fact]
    public void SubPixelRounding_NearBottom_ReturnsTrue()
    {
        // 7.9px gap is inside tolerance
        Assert.True(ScrollPendulum.ShouldAutoScroll(992.1, 500, 1500));
    }
}