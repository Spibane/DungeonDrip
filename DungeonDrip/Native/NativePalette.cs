using System.Numerics;
using DungeonDrip.Presentation;

namespace DungeonDrip.Native;

/// <summary>The native backend's answer to each <see cref="ColourRole"/>.</summary>
/// <remarks>
/// Not the ImGui values. Those were chosen to read on a dark ImGui window, and a game window is
/// whichever of the four themes the player picked - 0.58 grey is invisible on Light, and the green
/// washes out on Clear Blue.
///
/// The game solved this and the answer is visible in all of its own windows: near-white text with a
/// dark outline, plus a few saturated colours that read on any background <em>because the outline
/// carries the contrast rather than the fill</em>. So every role sets an outline as well as a fill,
/// and <see cref="ColourRole.Muted"/> is not a grey at all - "grey" is a dark-theme idea. It is the
/// same near-white at reduced alpha over a full-strength outline, which is what the game's own
/// tooltips do for their secondary lines.
///
/// These are written out rather than read from the UIColor sheet through
/// <c>KamiToolKit.Classes.ColorHelper.GetColor</c>, which would be better and is the intended
/// refinement: the sheet's values are the game's own rather than approximations of them. It needs
/// the right row ids, and a guessed row id is a worse answer than an explicit constant because it
/// looks authoritative.
///
/// <b>These want checking by eye in all four themes.</b> That is the check most likely to be
/// skipped, and a role legible on Dark but not on Light is not so much a wrong constant as a claim
/// the plugin has quietly stopped making.
/// </remarks>
internal static class NativePalette
{
    private static readonly Vector4 Text = new(1.00f, 1.00f, 1.00f, 1.00f);
    private static readonly Vector4 Outline = new(0.09f, 0.08f, 0.07f, 1.00f);

    /// <summary>Fill and outline for a role, ready to hand to a text node.</summary>
    public static (Vector4 Fill, Vector4 Outline) For(ColourRole role) => role switch
    {
        ColourRole.Warning => (new Vector4(1.00f, 0.78f, 0.29f, 1.00f), Outline),
        ColourRole.Good => (new Vector4(0.53f, 0.90f, 0.44f, 1.00f), Outline),
        ColourRole.Missing => (new Vector4(1.00f, 0.88f, 0.45f, 1.00f), Outline),
        ColourRole.NotOwned => (new Vector4(1.00f, 0.42f, 0.38f, 1.00f), Outline),
        ColourRole.Focus => (new Vector4(0.60f, 0.83f, 1.00f, 1.00f), Outline),

        // Alpha, not a darker fill. See the remarks: a grey that recedes on a dark background
        // advances on a light one, where the same white at 60% recedes on both.
        ColourRole.Muted => (Text with { W = 0.60f }, Outline),

        _ => (Text, Outline),
    };
}
