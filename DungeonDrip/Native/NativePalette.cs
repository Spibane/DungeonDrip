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
/// The semantic colours are not on that hook either, but they do come in two sets. What each one
/// means is the plugin's and no theme gets a vote on it - amber is a warning whatever the window is
/// made of - but a value is not a meaning, and the pale amber that carries on black is nearly
/// invisible on parchment. So there is a light set and a dark set of the same six ideas, and which
/// one is used follows the theme rather than being chosen.
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

    /// <summary>
    /// Whether the theme in use puts dark text on a light window.
    /// </summary>
    /// <remarks>
    /// Asked of the text colour rather than of the theme, because the theme is a number and what
    /// matters is what is behind the writing. Light text means a dark window and the other way
    /// round, which holds for all four of the game's themes and would hold for a fifth.
    ///
    /// Read every time rather than remembered: a theme can be changed at any moment from Character
    /// Configuration, and a remembered answer would leave the plugin the only thing on screen that
    /// had not noticed.
    /// </remarks>
    private static bool OnParchment()
    {
        var text = ColorHelper.GetColor(TextColourId);

        // Rec. 601, which is the cheap luminance and more than enough to tell one end from the other.
        return (text.X * 0.299f) + (text.Y * 0.587f) + (text.Z * 0.114f) < 0.5f;
    }

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

        // Darker and more saturated on the light themes, paler on the dark ones. The same six
        // meanings either way: what changes is which end of each reads against what is behind it.
        var parchment = OnParchment();

        return role switch
        {
            ColourRole.Warning => (parchment
                ? new Vector4(0.68f, 0.40f, 0.02f, 1.00f)
                : new Vector4(1.00f, 0.78f, 0.29f, 1.00f), outline),

            ColourRole.Good => (parchment
                ? new Vector4(0.10f, 0.42f, 0.10f, 1.00f)
                : new Vector4(0.35f, 0.75f, 0.30f, 1.00f), outline),

            ColourRole.Missing => (parchment
                ? new Vector4(0.62f, 0.42f, 0.02f, 1.00f)
                : new Vector4(0.95f, 0.75f, 0.20f, 1.00f), outline),

            ColourRole.NotOwned => (parchment
                ? new Vector4(0.66f, 0.08f, 0.06f, 1.00f)
                : new Vector4(0.90f, 0.25f, 0.22f, 1.00f), outline),

            ColourRole.Focus => (parchment
                ? new Vector4(0.09f, 0.28f, 0.62f, 1.00f)
                : new Vector4(0.30f, 0.60f, 0.95f, 1.00f), outline),

            ColourRole.Muted => (text with { W = MutedAlpha }, outline),

            _ => (text, outline),
        };
    }
}
