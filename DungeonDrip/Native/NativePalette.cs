using System.Numerics;
using DungeonDrip.Presentation;
using KamiToolKit.Classes;

namespace DungeonDrip.Native;

/// <summary>The native backend's answer to each <see cref="ColourRole"/>.</summary>
/// <remarks>
/// Ordinary text is not a colour of this plugin's choosing. It is read from the game's own colour
/// holder, which already holds whichever variant the player's theme calls for - the same source the
/// game's windows read, so the panels change with them rather than being told to. Any fixed pair
/// would be wrong for two of the four themes.
///
/// The semantic colours are deliberately not on that hook. The plugin means something specific by
/// each and no theme should get a vote on it - amber is a warning whatever the window is made of -
/// so they are written out, and darkened enough to carry on parchment as well as on black.
///
/// <see cref="ColourRole.Muted"/> is why the themed pair matters beyond legibility. "Grey" is a
/// dark-theme idea: a grey that recedes on black advances on parchment. Fading the theme's own text
/// colour towards its own background recedes on both.
/// </remarks>
internal static class NativePalette
{
    /// <summary>
    /// Colour holder ids for ordinary text and its outline, found by eye across the four themes.
    /// </summary>
    /// <remarks>
    /// These are a pair and only make sense as one: 1 is white on the dark themes and dark brown on
    /// the light ones, and 7 is its opposite - black on dark, cream on light. Either alone is
    /// unreadable half the time.
    ///
    /// The first attempt used 1 and 2, which looked right on the dark themes and was white on
    /// near-white everywhere else, because 2 is a light grey rather than the other end of the
    /// scale. The text id was never the problem; the outline was.
    /// </remarks>
    private const uint TextColourId = 1;

    private const uint OutlineColourId = 7;

    /// <summary>How much of the text colour a muted line keeps.</summary>
    private const float MutedAlpha = 0.60f;

    /// <summary>How far a muted tooltip line is faded towards what is behind it.</summary>
    private const float MutedFade = 0.45f;

    /// <summary>
    /// A role as one solid colour, for the places that cannot composite an alpha.
    /// </summary>
    /// <remarks>
    /// A text node is handed a colour and drawn over whatever is behind it, so
    /// <see cref="ColourRole.Muted"/> can simply be the theme's own text at reduced alpha there. Two
    /// places cannot do that: a tooltip, whose colours are macros inside a string, and a mark, whose
    /// colour is baked into the texture it is drawn from. Both need a colour that is already faded
    /// rather than one that fades when it lands.
    ///
    /// Mixing towards the outline colour is the same idea done arithmetically - that colour is the
    /// theme's opposite end, which is near enough what is behind the text.
    /// </remarks>
    public static Vector4 Solid(ColourRole role)
    {
        if (role != ColourRole.Muted)
            return For(role).Fill with { W = 1f };

        var text = ColorHelper.GetColor(TextColourId);
        var behind = ColorHelper.GetColor(OutlineColourId);

        return Vector4.Lerp(text, behind, MutedFade) with { W = 1f };
    }

    /// <summary>
    /// The colour a role's text is written in inside a tooltip, or null to leave it as it comes.
    /// </summary>
    /// <remarks>
    /// A tooltip is handed a string rather than a colour, so its colours are macros inside that
    /// string. The game takes two kinds: a row of its own colour table, which follows the theme, or
    /// a literal colour, which does not. <b>This uses literal ones, and that is the plugin's
    /// position rather than the easier road.</b> The semantic colours are deliberately off the
    /// theme's hook - amber is a warning whatever the window is made of - so a row that shifts with
    /// the theme would be answering a different question, and the colours a tooltip is written in
    /// have to be the ones the rows beside it are drawn in or the two disagree about what amber is.
    ///
    /// A colour table row was the first cut, on the strength of a probe reading. It was dropped
    /// once the values here turned out to be usable directly: the rows had to be found by eye per
    /// colour, only three were known, and the two that were not - <see cref="ColourRole.Muted"/>
    /// and <see cref="ColourRole.Focus"/> - are the ones a tooltip actually shows.
    ///
    /// <see cref="ColourRole.Default"/> returns null so the tooltip keeps whatever the game writes
    /// tooltips in. Naming a colour for ordinary text would mean picking one, and the game has
    /// already picked.
    /// </remarks>
    public static Vector4? TooltipFor(ColourRole role) =>
        role == ColourRole.Default ? null : Solid(role);

    /// <summary>Fill and outline for a role, ready to hand to a text node.</summary>
    public static (Vector4 Fill, Vector4 Outline) For(ColourRole role)
    {
        var text = ColorHelper.GetColor(TextColourId);
        var outline = ColorHelper.GetColor(OutlineColourId);

        return role switch
        {
            ColourRole.Warning => (new Vector4(1.00f, 0.78f, 0.29f, 1.00f), outline),
            ColourRole.Good => (new Vector4(0.35f, 0.75f, 0.30f, 1.00f), outline),
            ColourRole.Missing => (new Vector4(0.95f, 0.75f, 0.20f, 1.00f), outline),
            ColourRole.NotOwned => (new Vector4(0.90f, 0.25f, 0.22f, 1.00f), outline),
            ColourRole.Focus => (new Vector4(0.30f, 0.60f, 0.95f, 1.00f), outline),

            ColourRole.Muted => (text with { W = MutedAlpha }, outline),

            _ => (text, outline),
        };
    }
}
