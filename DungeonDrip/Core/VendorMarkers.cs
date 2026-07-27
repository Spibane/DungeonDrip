namespace DungeonDrip.Core;

/// <summary>What the vendor panel says about one piece of the stock in front of you.</summary>
/// <remarks>
/// Deliberately not <see cref="OwnershipSource"/>. That enum folds "definitely not collected" and
/// "no idea, the dresser has never been read" into a single <see cref="OwnershipSource.None"/>,
/// which is fine for the duty report - it refuses to draw at all without a snapshot - but is exactly
/// the distinction a vendor needs to keep. Buying a piece you already own wastes gil; walking past
/// one because the plugin guessed is worse.
/// </remarks>
public enum VendorMarker
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
/// Turns an ownership answer into what the vendor panel shows. Kept free of Dalamud and Lumina, like
/// <see cref="MissingItems"/>, so the argument below can be read - and tested - on its own.
/// </summary>
public static class VendorMarkers
{
    /// <param name="outfitCompleted">
    /// Only consulted for a piece held in an outfit set: whether any set holding it is finished.
    /// </param>
    public static VendorMarker For(OwnershipSource source, bool hasDresserData, bool outfitCompleted = false)
    {
        // Positive evidence stands on its own and needs no dresser snapshot behind it: inventory is
        // re-read every tick, and an armoire result is only ever recorded when the game genuinely
        // had the cabinet loaded.
        var marker = source switch
        {
            OwnershipSource.Dresser => VendorMarker.Dresser,
            OwnershipSource.Outfit => outfitCompleted ? VendorMarker.OutfitComplete : VendorMarker.Outfit,
            OwnershipSource.Armoire => VendorMarker.Armoire,
            OwnershipSource.Inventory => VendorMarker.Inventory,
            _ => VendorMarker.NotCollected,
        };

        // Absence of evidence is only evidence of absence once something was actually looked at.
        if (marker == VendorMarker.NotCollected && !hasDresserData)
            return VendorMarker.Unknown;

        return marker;
    }

    /// <summary>
    /// Whether this marker should be drawn in the warning colour rather than its own.
    /// </summary>
    /// <remarks>
    /// The staleness rule is inverted from the intuitive one, and it is worth stating why before
    /// someone "fixes" it. Dresser contents are near-monotonic: you add glamours far more often than
    /// you remove them. So an old snapshot's *positives* almost always still hold - if it said you
    /// owned a coat last month, you own it now - while its *negatives* are precisely what rots, as
    /// anything collected since the snapshot still reads as missing. The uncertain marker at a
    /// vendor is therefore "not collected", not "owned".
    ///
    /// Near-monotonic, not strictly: restoring a piece to inventory does take it out of the dresser.
    /// That error makes you skip something you actually need, which is mild and self-corrects the
    /// next time you open a dresser.
    /// </remarks>
    public static bool IsUncertain(VendorMarker marker, bool dresserIsStale) =>
        marker == VendorMarker.Unknown ||
        (marker == VendorMarker.NotCollected && dresserIsStale);

    public static string Describe(VendorMarker marker) => marker switch
    {
        VendorMarker.Dresser => "In your Glamour Dresser",
        VendorMarker.Outfit => "Part of a stored outfit set",
        VendorMarker.OutfitComplete => "Outfit completed",
        VendorMarker.Armoire => "In your Armoire",
        VendorMarker.Inventory => "Carried or equipped",
        VendorMarker.NotCollected => "Not collected",
        _ => "No dresser data - open your Glamour Dresser",
    };
}
