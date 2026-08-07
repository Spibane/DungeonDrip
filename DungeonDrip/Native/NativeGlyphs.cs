using DungeonDrip.Presentation;

namespace DungeonDrip.Native;

/// <summary>The native backend's answer to each <see cref="GlyphToken"/>.</summary>
/// <remarks>
/// <b>Text marks, and they are the spike's placeholder rather than the intended answer.</b> The
/// intended answer is curated game icon ids through <c>IconImageNode</c>, which would look native
/// and follow the player's theme for free. Choosing them means looking at candidate icons in the
/// game and picking the nine that read correctly, and an icon id guessed from a table is worse than
/// an obvious placeholder: a wrong id draws something plausible and confidently wrong, where a
/// letter is visibly a stand-in and gets replaced.
///
/// What this does let the spike answer is everything else - whether a row lays out, whether the
/// colours read against four themes, whether a tooltip attaches - none of which depends on the
/// mark being final.
///
/// The colour still carries most of the meaning here, as it does in the ImGui panels, so a
/// placeholder mark is less of a loss than it looks: a red x and a green star differ by colour
/// before they differ by shape.
/// </remarks>
internal static class NativeGlyphs
{
    /// <summary>A short mark for a token. One or two characters, so rows stay aligned.</summary>
    public static string For(GlyphToken token) => token switch
    {
        GlyphToken.Stored => "o",
        GlyphToken.InOutfit => "=",
        GlyphToken.OutfitComplete => "*",
        GlyphToken.OutfitPartial => "-",
        GlyphToken.InArmoire => "A",
        GlyphToken.Carried => "b",
        GlyphToken.WithRetainer => "R",
        GlyphToken.WornByRetainer => "r",
        GlyphToken.NotCollected => "x",

        GlyphToken.InBags => "b",
        GlyphToken.InArmoury => "A",
        GlyphToken.Equipped => "e",
        GlyphToken.InSaddlebag => "s",

        _ => "?",
    };
}
