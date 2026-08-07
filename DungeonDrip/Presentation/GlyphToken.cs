namespace DungeonDrip.Presentation;

/// <summary>The small mark at the head of a gear row, named by what it claims.</summary>
/// <remarks>
/// Nothing above a backend may name a shape. Both backends happen to draw the same FontAwesome
/// glyphs today - the native one renders them into textures rather than picking game icons, so the
/// readme's marker table stays true of either look - but that is a decision either could revisit,
/// and a presenter that said "briefcase" would be describing one rendering of a fact rather than
/// the fact.
///
/// Two pairs look interchangeable and are not. <see cref="InArmoire"/> is the Armoire, a place
/// pieces are put away for good; <see cref="InArmoury"/> is the armoury chest, where a gearset is
/// probably depending on the piece. They share a FontAwesome archive box today because that is what
/// was to hand, and a backend with two suitable icons should use two.
/// </remarks>
internal enum GlyphToken
{
    /// <summary>Put away in the Glamour Dresser.</summary>
    Stored,

    /// <summary>Inside a stored outfit set that still has gaps.</summary>
    InOutfit,

    /// <summary>Inside a stored outfit set with every slot filled.</summary>
    OutfitComplete,

    /// <summary>Inside some stored sets but not every set listing it - partway, not missing.</summary>
    OutfitPartial,

    /// <summary>Put away in the Armoire.</summary>
    InArmoire,

    /// <summary>Carried or worn, in no glamour box at all.</summary>
    Carried,

    /// <summary>In a retainer's bags - owned, but a trip away from being anything.</summary>
    WithRetainer,

    /// <summary>Worn by a retainer, which is not somewhere it can be looked for.</summary>
    WornByRetainer,

    /// <summary>Nowhere in the collection, and the collection was actually read.</summary>
    NotCollected,

    /// <summary>No snapshot exists, so no claim can honestly be made either way.</summary>
    Unknown,

    /// <summary>Loose in the bags - the only place a piece can be acted on with no preamble.</summary>
    InBags,

    /// <summary>In the armoury chest, where a gearset may quietly be depending on it.</summary>
    InArmoury,

    /// <summary>Worn, so it has to come off before it can go anywhere.</summary>
    Equipped,

    /// <summary>In a saddlebag, which has to be reached at a bell first.</summary>
    InSaddlebag,
}
