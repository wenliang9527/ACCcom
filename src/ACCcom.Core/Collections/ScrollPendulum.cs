namespace ACCcom.Core.Collections;

/// <summary>
/// Decides whether auto-scroll should follow new entries. The data panels call
/// ScrollToBottom on every collection change; without a "pinned to bottom"
/// check the view gets force-scrolled to the end even when the user has scrolled
/// up to inspect history. Logic is a pure function so it can live in Core and be
/// unit-tested without a WPF dependency.
/// </summary>
public static class ScrollPendulum
{
    /// <summary>How far (in pixels) from the true bottom we still treat the
    /// view as "pinned" — accounts for sub-pixel scroll positions and a small
    /// rounding tolerance before deciding to follow.</summary>
    public const double BottomTolerancePx = 8.0;

    /// <summary>
    /// True when the viewport is at (or near) the bottom and should follow new
    /// content; false when the user has scrolled up (they get to stay there).
    /// </summary>
    public static bool ShouldAutoScroll(double verticalOffset, double viewportHeight, double extentHeight)
    {
        // Guard: empty or not-yet-laid-out scroll viewers have zero extent.
        if (extentHeight <= 0) return true;
        return verticalOffset + viewportHeight >= extentHeight - BottomTolerancePx;
    }
}