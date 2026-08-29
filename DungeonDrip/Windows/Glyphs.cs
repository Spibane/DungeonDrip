using Dalamud.Interface;
using DungeonDrip.Presentation;

namespace DungeonDrip.Windows;

/// <summary>The FontAwesome glyph behind each <see cref="GlyphToken"/>.</summary>
/// <remarks>
/// These are the glyphs the panels have always drawn, unchanged. The readme's marker table
/// describes them by shape, so a change here is a change to a document as well as to a window.
///
/// <b>Both backends read this, which is why one table is enough.</b> The native panels draw these
/// same glyphs rather than curated game icon ids - see <c>Native.GlyphTextures</c> on why those ids
/// would have been guesses - so the marker vocabulary is one set of shapes and the readme can go on
/// naming them. It lives here rather than in <c>Presentation</c> because naming a shape is a
/// rendering decision, and the rule that no presenter may name one is what keeps it revisitable.
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

        // A ringed question mark rather than the bare one Unknown gets. The two sit on the same
        // list and mean different amounts of doubt, so they cannot be the same shape.
        GlyphToken.ArmoireUnread => FontAwesomeIcon.QuestionCircle,

        GlyphToken.InBags => FontAwesomeIcon.Briefcase,
        GlyphToken.InArmoury => FontAwesomeIcon.Archive,
        GlyphToken.Equipped => FontAwesomeIcon.User,
        GlyphToken.InSaddlebag => FontAwesomeIcon.Horse,

        _ => FontAwesomeIcon.Question,
    };
}
