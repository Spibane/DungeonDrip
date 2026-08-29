using DungeonDrip.Core;

namespace DungeonDrip.Presentation;

/// <summary>How a collection answer reaches a row: which glyph, and in what colour.</summary>
/// <remarks>
/// The mapping used to sit in the ImGui panels, next to the code that drew it, which was the right
/// place while there was one backend. It is a decision about meaning rather than about drawing -
/// that a partway outfit is a warning and not a red x, that a retainer gets a marker of its own -
/// and both backends have to reach the same answer or the same piece reads differently depending on
/// which look is switched on.
/// </remarks>
internal static class MarkerGlyph
{
    /// <remarks>
    /// The star marks a finished outfit and nothing else. A star is a reward everywhere else in the
    /// game, so it must never land on the pieces still missing.
    /// </remarks>
    public static GlyphToken For(CollectionMarker marker) => marker switch
    {
        CollectionMarker.Dresser => GlyphToken.Stored,
        CollectionMarker.Outfit => GlyphToken.InOutfit,
        CollectionMarker.OutfitComplete => GlyphToken.OutfitComplete,
        CollectionMarker.OutfitPartial => GlyphToken.OutfitPartial,
        CollectionMarker.Armoire => GlyphToken.InArmoire,
        CollectionMarker.Inventory => GlyphToken.Carried,
        CollectionMarker.Retainer => GlyphToken.WithRetainer,
        CollectionMarker.RetainerEquipped => GlyphToken.WornByRetainer,
        CollectionMarker.NotCollected => GlyphToken.NotCollected,
        CollectionMarker.ArmoireUnread => GlyphToken.ArmoireUnread,
        _ => GlyphToken.Unknown,
    };

    /// <summary>Where a carried piece is, for the panels whose rows are all unstored anyway.</summary>
    /// <remarks>
    /// The catch-all is the saddlebag rather than a retainer, and that is safe by construction
    /// rather than by luck: the store panels build their rows from
    /// <see cref="CarriedGear.Build"/> with an empty retainer list, because the subject is what can
    /// go into the box being stood at. A retainer's piece never reaches here. Should that ever
    /// change, this catch-all is the first thing that would start lying.
    /// </remarks>
    public static GlyphToken For(CarryLocation location) => location switch
    {
        CarryLocation.Bags => GlyphToken.InBags,
        CarryLocation.Armoury => GlyphToken.InArmoury,
        CarryLocation.Equipped => GlyphToken.Equipped,
        _ => GlyphToken.InSaddlebag,
    };

    /// <summary>
    /// What the glyph's colour is saying, which is not the same question as which glyph it is.
    /// </summary>
    /// <remarks>
    /// A partway outfit, an unread Armoire and an unread snapshot all come out as
    /// <see cref="ColourRole.Warning"/> rather than as the missing red: in every case the piece may
    /// well be in the box and it is the claim that is unsatisfied, so the hover is what settles it.
    /// The red is kept for the one state that means go and get one.
    /// </remarks>
    public static ColourRole ColourFor(CollectionMarker marker, bool uncertain) => marker switch
    {
        CollectionMarker.OutfitComplete => ColourRole.Good,
        CollectionMarker.NotCollected => uncertain ? ColourRole.Warning : ColourRole.NotOwned,
        CollectionMarker.OutfitPartial => ColourRole.Warning,
        CollectionMarker.ArmoireUnread => ColourRole.Warning,
        CollectionMarker.Unknown => ColourRole.Warning,
        _ => ColourRole.Muted,
    };

    /// <summary>The colour of the row's name, which says only whether the piece is needed.</summary>
    /// <remarks>
    /// <see cref="ColourRole.Default"/> for a missing piece rather than a colour of its own: the
    /// glyph carries the state, and any tint on the name would compete with it for meaning.
    /// </remarks>
    public static ColourRole NameColourFor(CollectionMarker marker)
    {
        if (CollectionMarkers.IsMissing(marker))
            return ColourRole.Default;

        return marker == CollectionMarker.Unknown ? ColourRole.Warning : ColourRole.Muted;
    }
}
