namespace DungeonDrip.Core;

/// <summary>What a panel says about one piece of gear the game is showing.</summary>
/// <remarks>
/// Not <see cref="OwnershipSource"/>, which folds "not collected" and "never read the dresser" into
/// one value. The duty report can afford that because it refuses to draw without a snapshot; a
/// panel riding beside a game window shows the list anyway, so it has to keep the two apart.
/// </remarks>
public enum CollectionMarker
{
    /// <summary>Nowhere in the collection, and the collection was actually read.</summary>
    NotCollected,

    Dresser,

    /// <summary>Held inside a stored outfit set that still has gaps in it.</summary>
    Outfit,

    /// <summary>Held inside a stored outfit set with every slot filled.</summary>
    OutfitComplete,

    /// <summary>
    /// Held inside at least one stored outfit set, but not every set that lists it - so
    /// <see cref="OutfitOwnershipMode.AllOutfits"/> does not call it collected.
    /// </summary>
    /// <remarks>
    /// Missing, like <see cref="NotCollected"/>, and grouped and counted with it. It exists as its
    /// own value because the two are not the same problem: this piece is already in the box and the
    /// shortfall is a rule the user chose, whereas "not collected" means go and get one. A flat "not
    /// owned" on a piece the user can see sitting in their dresser reads as a bug rather than as the
    /// setting doing what it says.
    /// </remarks>
    OutfitPartial,

    Armoire,
    Inventory,

    /// <summary>
    /// In a retainer's bags, as of the last time they were open.
    /// </summary>
    /// <remarks>
    /// Its own value rather than folded into <see cref="Inventory"/>, which it resembles - both mean
    /// "owned, in no glamour box". The difference is reach: a piece in the bags can be put away on the
    /// spot, and one with a retainer needs a trip to a bell before it is anything at all. That is worth
    /// a marker of its own on a list being read at a vendor.
    /// </remarks>
    Retainer,

    /// <summary>
    /// Worn by a retainer, which is not somewhere it can be looked for.
    /// </summary>
    /// <remarks>
    /// Split off from <see cref="Retainer"/> after the two together cost somebody five minutes of
    /// searching a retainer's bags for a coat the retainer had on. Every marker in this vocabulary is
    /// read as "here is where to find it", so one that names the wrong place is worse than none.
    /// </remarks>
    RetainerEquipped,

    /// <summary>
    /// Nowhere the plugin looked - but the Armoire accepts it and the Armoire's snapshot cannot be
    /// relied on.
    /// </summary>
    /// <remarks>
    /// Missing, and counted and grouped with <see cref="NotCollected"/>, because that is what it
    /// almost always is: most of what the Armoire accepts is not in anybody's Armoire, and a piece
    /// quietly dropped off the list would be one nobody goes looking for.
    ///
    /// It is its own value so the doubt can be drawn and worded. The dresser is re-read on any
    /// glamour operation, so its snapshot is nearly always current; the Armoire only loads at an
    /// inn, so a character can play for months with a fresh dresser and an Armoire last read a
    /// patch ago - and every piece it accepts was being called "not owned" on the strength of it.
    ///
    /// Not <see cref="Unknown"/>, which is the refusal to answer at all. Here half the answer is
    /// good: the dresser really has been read and really does not have this.
    /// </remarks>
    ArmoireUnread,

    /// <summary>No dresser snapshot exists, so no claim can honestly be made either way.</summary>
    Unknown,
}

/// <summary>
/// Turns an ownership answer into what a panel shows. Free of Dalamud and Lumina, like
/// <see cref="MissingItems"/>, so it can be reasoned about on its own.
/// </summary>
public static class CollectionMarkers
{
    /// <summary>
    /// The marker for one piece, given what the ownership decision said and whether anything was
    /// actually read.
    /// </summary>
    /// <remarks>
    /// The order of the four steps is the point. A positive answer is taken at face value first,
    /// because it needs no snapshot behind it; then the outfit shortfall, which is itself read off a
    /// snapshot and so cannot be the no-data case; then the bare negative downgraded to
    /// <see cref="CollectionMarker.Unknown"/>; and only what survives all three can be doubted on
    /// the Armoire's account. Reordering any of that makes a marker claim something nothing looked
    /// at.
    ///
    /// The Armoire step comes last of the doubts because it is the narrowest: with no dresser
    /// snapshot at all there is nothing to say about either box, and naming one of them would be
    /// picking the smaller of two holes to point at.
    /// </remarks>
    /// <param name="outfitCompleted">
    /// Only consulted for a piece held in an outfit set: whether any set holding it is finished.
    /// </param>
    /// <param name="outfitShortfall">
    /// Only consulted for a piece the ownership rule rejected: whether a stored outfit holds it
    /// anyway and the rejection was <see cref="OutfitOwnershipMode.AllOutfits"/> asking for the rest.
    /// </param>
    /// <param name="armoireInDoubt">
    /// Only consulted for a piece nothing was found for: whether the Armoire would take this piece,
    /// is being compared against, and was last read too long ago to settle it. Three facts the
    /// caller has and this cannot see - the item's storage kind, the scope setting, and the
    /// snapshot's age - so they are collapsed into one answer on the way in.
    /// </param>
    public static CollectionMarker For(
        OwnershipSource source,
        bool hasDresserData,
        bool outfitCompleted = false,
        bool outfitShortfall = false,
        bool armoireInDoubt = false)
    {
        // Positive evidence needs no dresser snapshot behind it: inventory is re-read every tick, and
        // an armoire or retainer result is only recorded when the game had that store loaded.
        var marker = source switch
        {
            OwnershipSource.Dresser => CollectionMarker.Dresser,
            OwnershipSource.Outfit => outfitCompleted ? CollectionMarker.OutfitComplete : CollectionMarker.Outfit,
            OwnershipSource.Armoire => CollectionMarker.Armoire,
            OwnershipSource.Inventory => CollectionMarker.Inventory,
            OwnershipSource.Retainer => CollectionMarker.Retainer,
            OwnershipSource.RetainerEquipped => CollectionMarker.RetainerEquipped,
            _ => CollectionMarker.NotCollected,
        };

        if (marker != CollectionMarker.NotCollected)
            return marker;

        // Before the no-snapshot case, because a shortfall was read off a snapshot by definition -
        // it takes dresser contents to know one stored outfit holds the piece and another does not.
        if (outfitShortfall)
            return CollectionMarker.OutfitPartial;

        // Absence of evidence is only evidence of absence once something was actually looked at.
        if (!hasDresserData)
            return CollectionMarker.Unknown;

        // The dresser having been read is not enough for a piece the Armoire would also take: with
        // both stores compared, "not owned" is a claim about both boxes and only one of them was
        // opened recently enough to make it.
        return armoireInDoubt ? CollectionMarker.ArmoireUnread : marker;
    }

    /// <summary>
    /// Whether this marker means the piece still wants doing something about.
    /// </summary>
    /// <remarks>
    /// The counts, the groupings and the show-owned filter all ask this rather than comparing
    /// against <see cref="CollectionMarker.NotCollected"/> by hand, which is what let
    /// <see cref="CollectionMarker.OutfitPartial"/> be added without hunting for the comparisons
    /// that meant "missing" all along. <see cref="CollectionMarker.Unknown"/> is not missing: it is
    /// the refusal to say either way.
    ///
    /// <see cref="CollectionMarker.ArmoireUnread"/> is missing, unlike that one, and the difference
    /// is which way being wrong hurts. Nothing at all was read for an Unknown, so listing every
    /// piece in the game would be the alternative; an ArmoireUnread piece is one the dresser really
    /// does not have, and dropping it off the list to be safe would hide most of a duty's
    /// accessories from somebody who has never opened an inn wardrobe.
    /// </remarks>
    public static bool IsMissing(CollectionMarker marker) =>
        marker is CollectionMarker.NotCollected or CollectionMarker.OutfitPartial
            or CollectionMarker.ArmoireUnread;

    /// <summary>
    /// Whether this marker should be drawn in the warning colour rather than its own.
    /// </summary>
    /// <remarks>
    /// Staleness marks "not collected", not "owned", which reads backwards until the reason is clear:
    /// dresser contents are near-monotonic, so an old snapshot's positives still hold while its
    /// negatives are exactly what rots. Do not invert this.
    ///
    /// <see cref="CollectionMarker.ArmoireUnread"/> is uncertain whatever the dresser's age, since
    /// the doubt in it is the Armoire's and was already decided before the marker was made.
    /// </remarks>
    public static bool IsUncertain(CollectionMarker marker, bool dresserIsStale) =>
        marker is CollectionMarker.Unknown or CollectionMarker.ArmoireUnread ||
        (IsMissing(marker) && dresserIsStale);

    /// <param name="outfitsStored">
    /// For <see cref="CollectionMarker.OutfitPartial"/>: how many of the sets listing the piece are
    /// stored holding it, out of <paramref name="outfitsTotal"/>. The whole point of that marker is
    /// the number, so it is worth the two arguments the other markers ignore.
    /// </param>
    public static string Describe(CollectionMarker marker, int outfitsStored = 0, int outfitsTotal = 0) => marker switch
    {
        CollectionMarker.Dresser => "In your Glamour Dresser",
        CollectionMarker.Outfit => "Part of a stored outfit set",
        CollectionMarker.OutfitComplete => "Outfit completed",
        CollectionMarker.Armoire => "In your Armoire",
        CollectionMarker.Inventory => "Carried or equipped",
        CollectionMarker.Retainer => "In a retainer's bags",
        CollectionMarker.RetainerEquipped => "Worn by one of your retainers",
        CollectionMarker.NotCollected => "Not collected",

        // Says what was looked at rather than what was not found, because the finding is the part
        // that might be wrong and the reader is the only one who can settle it.
        CollectionMarker.ArmoireUnread =>
            "Not in your Glamour Dresser - and your Armoire may already have it. " +
            "Open the Armoire at an inn to be sure.",

        CollectionMarker.OutfitPartial => outfitsTotal > 1
            ? $"Stored in {outfitsStored} of the {outfitsTotal} outfit sets that use it - " +
              "your settings only count it once every one of them is stored"
            : "Stored in an outfit set, but not in every set that uses it",

        _ => "No dresser data - open your Glamour Dresser",
    };
}
