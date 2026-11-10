namespace IntegratedModManager.Core.Layout;

/// <summary>Where a getting-started tip opens relative to its target.</summary>
public enum TipPlacement { BottomLeft, Bottom, Top, Center }

/// <summary>
/// Places a teaching tip so it fits in the window (the getting-started tips, J4-b; fixed after J8): below or above
/// its target, never beside a wide one, and in the middle of the page without pointing at anything when neither fits
/// or the target is not on screen.
/// </summary>
public static class TipPlacementPolicy
{
    // How much of a tall target's top must be on screen for the tip to point at it from above.
    private const double TopEdge = 48;

    /// <param name="targetLeft">The target's position and size in window coordinates.</param>
    /// <param name="windowWidth">The window's content size.</param>
    /// <param name="tipWidth">The tip's size, a little larger than its content.</param>
    /// <param name="preferBelowLeft">Open below and to the left (a target at the window's top right).</param>
    public static TipPlacement Choose(double targetLeft, double targetTop, double targetWidth, double targetHeight,
        double windowWidth, double windowHeight, double tipWidth, double tipHeight, bool preferBelowLeft = false)
    {
        double right = targetLeft + targetWidth, bottom = targetTop + targetHeight;
        bool sideways = targetWidth > 0 && targetHeight > 0 && targetLeft >= 0 && right <= windowWidth;
        if (!sideways || tipWidth > windowWidth) return TipPlacement.Center;

        // Below needs the whole target on screen; above only its top edge (a tall card, such as the opened path
        // settings, is pointed at from above).
        double below = windowHeight - bottom, above = targetTop;
        if (targetTop >= 0 && bottom <= windowHeight && below >= tipHeight)
        {
            // BottomLeft lines the tip's right edge up with the target's: it needs the room to the target's left.
            if (preferBelowLeft && right >= tipWidth) return TipPlacement.BottomLeft;
            // Bottom and Top centre the tip on the target; the tip is kept inside the window sideways.
            return TipPlacement.Bottom;
        }
        bool topEdgeVisible = targetTop >= 0 && targetTop + Math.Min(targetHeight, TopEdge) <= windowHeight;
        return topEdgeVisible && above >= tipHeight ? TipPlacement.Top : TipPlacement.Center;
    }
}
