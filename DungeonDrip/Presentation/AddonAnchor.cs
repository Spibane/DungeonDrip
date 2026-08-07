using System;
using System.Numerics;
using DungeonDrip.Core;

namespace DungeonDrip.Presentation;

/// <summary>Where a panel goes so that it sits beside a game window rather than over it.</summary>
/// <remarks>
/// Plain arithmetic on coordinates the caller has already read, rather than something that reads
/// the addon itself. Both backends place panels and both would otherwise write this out again, but
/// they do not agree on how to get at the addon or the screen: the ImGui panels are inside a draw
/// callback with <c>ImGui.GetIO()</c> to hand, and a native panel is on the framework tick with
/// neither. Taking the four numbers as arguments is what lets one copy serve both.
///
/// Nothing here is multiplied by a UI scale. Screen pixels are screen pixels; scale belongs to
/// glyphs, and applying it to a position is how these panels drift away from the window they are
/// meant to be beside.
/// </remarks>
internal static class AddonAnchor
{
    private const float Gap = 6f;

    /// <summary>Top-left corner for a panel of <paramref name="ownSize"/> beside an addon.</summary>
    /// <param name="addonPos">The addon's top-left, in screen pixels.</param>
    /// <param name="addonWidth">The addon's width on screen, scale already applied.</param>
    /// <param name="display">The screen, so a panel cannot be placed off the edge of it.</param>
    public static Vector2 Beside(
        Vector2 addonPos, float addonWidth, PanelSide side, Vector2 ownSize, Vector2 display)
    {
        var toRight = side switch
        {
            PanelSide.Left => false,
            PanelSide.Right => true,
            _ => addonPos.X + addonWidth + Gap + ownSize.X <= display.X,
        };

        var x = toRight
            ? addonPos.X + addonWidth + Gap
            : addonPos.X - ownSize.X - Gap;

        // Clamped against the panel's own size: an addon jammed against an edge would otherwise
        // push it off screen.
        return new Vector2(
            Math.Clamp(x, 0f, Math.Max(0f, display.X - ownSize.X)),
            Math.Clamp(addonPos.Y, 0f, Math.Max(0f, display.Y - ownSize.Y)));
    }
}
