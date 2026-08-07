using System.Numerics;
using DungeonDrip.Presentation;
using KamiToolKit.Classes;

namespace DungeonDrip.Native;

/// <summary>The native backend's answer to each <see cref="ColourRole"/>.</summary>
/// <remarks>
/// Not the ImGui values, and not fixed values of its own either. The first cut here was white text
/// with a dark outline, which is right on the dark themes and unreadable on the light ones: pale
/// text on parchment. Whichever fixed pair is chosen, two of the four themes get the wrong one.
///
/// So ordinary text is not chosen at all. <see cref="ColorHelper.GetColor"/> reads the game's live
/// colour holder, which already holds whichever variant the player's theme calls for - the same
/// source the game's own windows read, so the panel changes with them rather than being told to.
///
/// The semantic colours stay written out. They are saturated enough to carry on any of the four
/// backgrounds, and the plugin means something specific by each of them that no theme should get a
/// vote on - amber is a warning whatever the window is made of.
///
/// <see cref="ColourRole.Muted"/> is the interesting one, and it is why this cannot just be a table
/// of constants. "Grey" is a dark-theme idea: a grey that recedes on black advances on parchment.
/// Taking the theme's own text colour and dropping its alpha recedes on both, because it is moving
/// towards whatever the background happens to be.
/// </remarks>
internal static class NativePalette
{
    /// <summary>
    /// Colour holder ids for ordinary text and its outline.
    /// </summary>
    /// <remarks>
    /// <b>Worth confirming by eye rather than trusting.</b> These are the conventional ids for the
    /// default text pair and they are read live, so they follow the theme - but if the panel's
    /// ordinary rows come out the wrong colour on any theme while the amber and the red look right,
    /// these two numbers are the reason and nothing else here is.
    /// </remarks>
    private const uint TextColourId = 1;

    private const uint OutlineColourId = 2;

    /// <summary>How much of the theme's text colour a muted line keeps.</summary>
    private const float MutedAlpha = 0.60f;

    /// <summary>Fill and outline for a role, ready to hand to a text node.</summary>
    public static (Vector4 Fill, Vector4 Outline) For(ColourRole role)
    {
        var outline = ColorHelper.GetColor(OutlineColourId);

        return role switch
        {
            ColourRole.Warning => (new Vector4(1.00f, 0.78f, 0.29f, 1.00f), outline),
            ColourRole.Good => (new Vector4(0.35f, 0.75f, 0.30f, 1.00f), outline),
            ColourRole.Missing => (new Vector4(0.95f, 0.75f, 0.20f, 1.00f), outline),
            ColourRole.NotOwned => (new Vector4(0.90f, 0.25f, 0.22f, 1.00f), outline),
            ColourRole.Focus => (new Vector4(0.30f, 0.60f, 0.95f, 1.00f), outline),

            ColourRole.Muted => (ColorHelper.GetColor(TextColourId) with { W = MutedAlpha }, outline),

            _ => (ColorHelper.GetColor(TextColourId), outline),
        };
    }
}
