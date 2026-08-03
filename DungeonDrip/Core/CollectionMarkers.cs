namespace DungeonDrip.Core;

/// <summary>What a panel says about one piece of gear the game is showing you.</summary>
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

    /// <summary>No dresser snapshot exists, so no claim can honestly be made either way.</summary>
    Unknown,
}

/// <summary>
/// Turns an ownership answer into what a panel shows. Free of Dalamud and Lumina, like
/// <see cref="MissingItems"/>, so it can be reasoned about on its own.
/// </summary>
public static class CollectionMarkers
{
    /// <param name="outfitCompleted">
    /// Only consulted for a piece held in an outfit set: whether any set holding it is finished.
    /// </param>
    /// <param name="outfitShortfall">
    /// Only consulted for a piece the ownership rule rejected: whether a stored outfit holds it
    /// anyway and the rejection was <see cref="OutfitOwnershipMode.AllOutfits"/> asking for the rest.
    /// </param>
    public static CollectionMarker For(
        OwnershipSource source,
        bool hasDresserData,
        bool outfitCompleted = false,
        bool outfitShortfall = false)
    {
        // Positive evidence needs no dresser snapshot behind it: inventory is re-read every tick,
        // and an armoire result is only recorded when the game had the cabinet loaded.
        var marker = source switch
        {
            OwnershipSource.Dresser => CollectionMarker.Dresser,
            OwnershipSource.Outfit => outfitCompleted ? CollectionMarker.OutfitComplete : CollectionMarker.Outfit,
            OwnershipSource.Armoire => CollectionMarker.Armoire,
            OwnershipSource.Inventory => CollectionMarker.Inventory,
            _ => CollectionMarker.NotCollected,
        };

        if (marker != CollectionMarker.NotCollected)
            return marker;

        // Before the no-snapshot case, because a shortfall was read off a snapshot by definition -
        // it takes dresser contents to know one stored outfit holds the piece and another does not.
        if (outfitShortfall)
            return CollectionMarker.OutfitPartial;

        // Absence of evidence is only evidence of absence once something was actually looked at.
        return hasDresserData ? marker : CollectionMarker.Unknown;
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
    /// </remarks>
    public static bool IsMissing(CollectionMarker marker) =>
        marker is CollectionMarker.NotCollected or CollectionMarker.OutfitPartial;

    /// <summary>
    /// Whether this marker should be drawn in the warning colour rather than its own.
    /// </summary>
    /// <remarks>
    /// Staleness marks "not collected", not "owned", which reads backwards until you see why:
    /// dresser contents are near-monotonic, so an old snapshot's positives still hold while its
    /// negatives are exactly what rots. Do not invert this.
    /// </remarks>
    public static bool IsUncertain(CollectionMarker marker, bool dresserIsStale) =>
        marker == CollectionMarker.Unknown ||
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
        CollectionMarker.NotCollected => "Not collected",

        CollectionMarker.OutfitPartial => outfitsTotal > 1
            ? $"Stored in {outfitsStored} of the {outfitsTotal} outfit sets that use it - " +
              "your settings only count it once every one of them is stored"
            : "Stored in an outfit set, but not in every set that uses it",

        _ => "No dresser data - open your Glamour Dresser",
    };
}
