using System.Numerics;
using DungeonDrip.Presentation;

namespace DungeonDrip.Native;

/// <summary>The native backend's answer to each <see cref="ColourRole"/>.</summary>
/// <remarks>
/// <b>Ordinary text is white with a dark outline, and that is a known limit rather than a
/// choice.</b> It reads on the dark themes and reads poorly on the light ones, where the game
/// itself uses dark text on parchment. Any fixed pair is wrong for two of the four themes.
///
/// Reading the pair from the game's own colour holder was tried, since that holder is theme-aware
/// and is where the game's windows get their colours. It needs the right colour ids and the
/// conventional ones are not them - they came back white for both fill and outline, which on a
/// light theme is white on white. The mechanism is right and the two numbers are not known;
/// finding them is what this is waiting on, not a different approach.
///
/// The semantic colours are deliberately not on that hook. The plugin means something specific by
/// each and no theme should get a vote on it - amber is a warning whatever the window is made of -
/// so they are written out, and darkened enough to carry on parchment as well as on black.
///
/// <see cref="ColourRole.Muted"/> is the one that suffers most from the fixed pair. "Grey" is a
/// dark-theme idea: a grey that recedes on black advances on parchment. Fading the text colour
/// towards the background would recede on both, which is another thing the colour ids would buy.
/// </remarks>
internal static class NativePalette
{
    /// <summary>How much of the text colour a muted line keeps.</summary>
    private const float MutedAlpha = 0.60f;

    private static readonly Vector4 Text = new(1.00f, 1.00f, 1.00f, 1.00f);
    private static readonly Vector4 Outline = new(0.09f, 0.08f, 0.07f, 1.00f);

    /// <summary>Fill and outline for a role, ready to hand to a text node.</summary>
    public static (Vector4 Fill, Vector4 Outline) For(ColourRole role) => role switch
    {
        ColourRole.Warning => (new Vector4(1.00f, 0.78f, 0.29f, 1.00f), Outline),
        ColourRole.Good => (new Vector4(0.35f, 0.75f, 0.30f, 1.00f), Outline),
        ColourRole.Missing => (new Vector4(0.95f, 0.75f, 0.20f, 1.00f), Outline),
        ColourRole.NotOwned => (new Vector4(0.90f, 0.25f, 0.22f, 1.00f), Outline),
        ColourRole.Focus => (new Vector4(0.30f, 0.60f, 0.95f, 1.00f), Outline),

        ColourRole.Muted => (Text with { W = MutedAlpha }, Outline),

        _ => (Text, Outline),
    };
}
