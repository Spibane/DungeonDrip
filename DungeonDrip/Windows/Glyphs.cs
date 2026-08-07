using Dalamud.Interface;
using DungeonDrip.Presentation;

namespace DungeonDrip.Windows;

/// <summary>The ImGui backend's answer to each <see cref="GlyphToken"/>.</summary>
/// <remarks>
/// These are the FontAwesome glyphs the panels have always drawn, unchanged. The readme's marker
/// table describes them by shape, so a change here is a change to a document as well as to a
/// window.
///
/// Two tokens share a glyph: <see cref="GlyphToken.InArmoire"/> and
/// <see cref="GlyphToken.InArmoury"/> are both an archive box, and
/// <see cref="GlyphToken.Carried"/> and <see cref="GlyphToken.InBags"/> are both a briefcase.
/// Neither pair appears on the same list as the other, so nothing is currently ambiguous on screen -
/// but they are separate tokens so that a backend with better icons is not stuck with the collision.
/// </remarks>
internal static class Glyphs
{
    public static FontAwesomeIcon For(GlyphToken token) => token switch
    {
        GlyphToken.Stored => FontAwesomeIcon.Check,
        GlyphToken.InOutfit => FontAwesomeIcon.LayerGroup,
        GlyphToken.OutfitComplete => FontAwesomeIcon.Star,

        // Half-filled, not a cross and not a star: the outfit-shaped states share the layered look
        // and this one is the one that is partway there.
        GlyphToken.OutfitPartial => FontAwesomeIcon.Adjust,
        GlyphToken.InArmoire => FontAwesomeIcon.Archive,
        GlyphToken.Carried => FontAwesomeIcon.Briefcase,

        // Not a second case. A retainer is somewhere to be travelled to, and the figure says so.
        GlyphToken.WithRetainer => FontAwesomeIcon.UserTag,

        // The dressed figure, because this one is not in any bag: the retainer has it on.
        GlyphToken.WornByRetainer => FontAwesomeIcon.UserTie,
        GlyphToken.NotCollected => FontAwesomeIcon.Times,

        GlyphToken.InBags => FontAwesomeIcon.Briefcase,
        GlyphToken.InArmoury => FontAwesomeIcon.Archive,
        GlyphToken.Equipped => FontAwesomeIcon.User,
        GlyphToken.InSaddlebag => FontAwesomeIcon.Horse,

        _ => FontAwesomeIcon.Question,
    };
}
