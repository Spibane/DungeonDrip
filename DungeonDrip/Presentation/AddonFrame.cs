using DungeonDrip.Game;

namespace DungeonDrip.Presentation;

/// <summary>How much of a game window's rectangle is decorative frame rather than window.</summary>
/// <remarks>
/// A panel is placed against the rectangle the game reports for the window it accompanies, and on
/// most windows that rectangle is the window. On some it is not: the art has an ornamental border
/// that the rectangle includes and the eye does not read as part of the window at all. A panel
/// placed against the rectangle then sits a visible distance out from the window it is supposed to
/// belong to, and runs past the bottom of it.
///
/// The numbers are measured by eye against the art, because there is nothing in the addon that
/// distinguishes frame from window - the border is drawn by the same nodes as the rest of it. That
/// makes them the kind of value a graphics update could invalidate, which is why they are here in
/// one table rather than spread over the panels that need them.
///
/// Shared by both backends for the same reason <see cref="AddonAnchor"/> is: where a window's edge
/// appears to be is a fact about the game rather than about how the panel beside it is drawn, and
/// two copies of a hand-measured number are two chances to correct only one of them.
/// </remarks>
internal static class AddonFrame
{
    /// <summary>Pixels of frame on each side of a window, or nothing for a window that has none.</summary>
    public static FrameInset For(string addonName) => addonName switch
    {
        // The Glamour Dresser. Its border is the widest of any window the plugin sits beside - the
        // corner flourishes alone are most of it - and it is the only one where the difference is
        // large enough to look like a mistake.
        //
        // All four numbers are the visible edge rather than the reported one, so the top moves the
        // panel down and shortens it by the same amount rather than only moving it: a panel that
        // started level with the window and still ended below it would have traded one misalignment
        // for another.
        DresserAddWatcher.AddonName => new FrameInset(Top: 10f, Right: 22f, Bottom: 24f),

        _ => default,
    };
}

/// <summary>Frame widths on the four sides of a window, in screen pixels.</summary>
/// <remarks>
/// A record struct rather than four arguments, so a call site cannot quietly pass a bottom where a
/// right belongs. The default is a window with no frame to speak of, which is most of them.
/// </remarks>
internal readonly record struct FrameInset(
    float Left = 0f, float Top = 0f, float Right = 0f, float Bottom = 0f);
