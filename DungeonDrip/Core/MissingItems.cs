using System.Collections.Generic;
using System.Linq;
using DungeonDrip.Game;

namespace DungeonDrip.Core;

public enum OwnershipSource
{
    None,
    Dresser,
    Outfit,
    Armoire,
    Inventory,
}

/// <summary>
/// The ownership decision, kept free of Dalamud and Lumina so it can be reasoned about (and tested)
/// on its own.
/// </summary>
public static class MissingItems
{
    /// <param name="setsContainingItem">
    /// Every outfit set in the game that lists this piece - only consulted in
    /// <see cref="OutfitOwnershipMode.AllOutfits"/>.
    /// </param>
    public static OwnershipSource Resolve(
        uint itemId,
        OwnershipView view,
        IReadOnlySet<uint> setsContainingItem,
        OutfitOwnershipMode mode,
        CollectionScope scope = CollectionScope.Both)
    {
        var checkDresser = scope != CollectionScope.ArmoireOnly;
        var checkArmoire = scope != CollectionScope.DresserOnly;

        if (checkDresser && view.DresserDirect.Contains(itemId))
            return OwnershipSource.Dresser;

        if (checkArmoire && view.Armoire.Contains(itemId))
            return OwnershipSource.Armoire;

        if (checkDresser && view.DresserOutfits.TryGetValue(itemId, out var storedIn) && storedIn.Count > 0)
        {
            var satisfied = mode == OutfitOwnershipMode.AnyOutfit ||
                            setsContainingItem.All(storedIn.Contains);

            if (satisfied)
                return OwnershipSource.Outfit;
        }

        if (view.Inventory != null && view.Inventory.Contains(itemId))
            return OwnershipSource.Inventory;

        return OwnershipSource.None;
    }

    /// <summary>
    /// How many of the outfit sets listing a piece are stored holding it, out of how many exist.
    /// </summary>
    /// <remarks>
    /// Only interesting once <see cref="Resolve"/> has said <see cref="OwnershipSource.None"/> in
    /// <see cref="OutfitOwnershipMode.AllOutfits"/>: a non-zero first number there is the difference
    /// between "go and get one" and "you have one, your own rule wants the rest", which a flat "not
    /// owned" was hiding on a piece the user could see in their dresser.
    ///
    /// Stored is counted by intersection rather than off <c>storedIn.Count</c> alone, so the numbers
    /// stay a subset and a total: a set that holds the piece but is not in the game's outfit sheet
    /// cannot push the first number above the second.
    /// </remarks>
    public static (int Stored, int Total) OutfitStanding(
        uint itemId, OwnershipView view, IReadOnlySet<uint> setsContainingItem)
    {
        var total = setsContainingItem.Count;

        if (total == 0 || !view.DresserOutfits.TryGetValue(itemId, out var storedIn))
            return (0, total);

        return (setsContainingItem.Count(storedIn.Contains), total);
    }

    /// <summary>
    /// The outfit standing behind a piece <see cref="Resolve"/> rejected, or zeroes when the
    /// rejection had nothing to do with outfits.
    /// </summary>
    /// <remarks>
    /// One place, called by every surface that shows a marker, so they cannot disagree about when a
    /// shortfall exists. Zeroes for anything else: a piece nobody stored has no shortfall to report,
    /// and neither does one rejected in <see cref="OutfitOwnershipMode.AnyOutfit"/> - which never
    /// rejects a stored piece - or with the dresser out of scope entirely.
    /// </remarks>
    public static (int Stored, int Total) Shortfall(
        uint itemId,
        OwnershipView view,
        IReadOnlySet<uint> setsContainingItem,
        OwnershipSource source,
        OutfitOwnershipMode mode,
        CollectionScope scope)
    {
        if (source != OwnershipSource.None ||
            mode != OutfitOwnershipMode.AllOutfits ||
            scope == CollectionScope.ArmoireOnly)
        {
            return (0, 0);
        }

        var (stored, total) = OutfitStanding(itemId, view, setsContainingItem);
        return stored > 0 && stored < total ? (stored, total) : (0, 0);
    }

    public static string Describe(OwnershipSource source) => source switch
    {
        OwnershipSource.Dresser => "In your Glamour Dresser",
        OwnershipSource.Outfit => "Part of a stored outfit set",
        OwnershipSource.Armoire => "In your Armoire",
        OwnershipSource.Inventory => "Carried or equipped",
        _ => "Not collected",
    };
}
