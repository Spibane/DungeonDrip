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
    public static CollectionMarker For(OwnershipSource source, bool hasDresserData, bool outfitCompleted = false)
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

        // Absence of evidence is only evidence of absence once something was actually looked at.
        if (marker == CollectionMarker.NotCollected && !hasDresserData)
            return CollectionMarker.Unknown;

        return marker;
    }

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
        (marker == CollectionMarker.NotCollected && dresserIsStale);

    public static string Describe(CollectionMarker marker) => marker switch
    {
        CollectionMarker.Dresser => "In your Glamour Dresser",
        CollectionMarker.Outfit => "Part of a stored outfit set",
        CollectionMarker.OutfitComplete => "Outfit completed",
        CollectionMarker.Armoire => "In your Armoire",
        CollectionMarker.Inventory => "Carried or equipped",
        CollectionMarker.NotCollected => "Not collected",
        _ => "No dresser data - open your Glamour Dresser",
    };
}
