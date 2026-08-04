using System.Collections.Generic;
using System.Linq;
using DungeonDrip.Game;

namespace DungeonDrip.Core;

/// <summary>
/// Where a piece was found, or <see cref="None"/> when it was not.
/// </summary>
/// <remarks>
/// The order is the precedence <see cref="MissingItems.Resolve"/> answers in, which runs from the
/// stores a glamour can actually be worn out of to the places a piece is merely held. Nothing here
/// distinguishes "not collected" from "the dresser was never read" - the surfaces that need that
/// distinction use <see cref="CollectionMarker"/>, which is this plus the refusal to say.
/// </remarks>
public enum OwnershipSource
{
    None,
    Dresser,
    Outfit,
    Armoire,
    Inventory,

    /// <summary>In a retainer's bags, as of the last time they were open.</summary>
    Retainer,

    /// <summary>
    /// Worn by a retainer, which is not the same as being in their bags.
    /// </summary>
    /// <remarks>
    /// Its own answer because the other one names the wrong place. "With a retainer" is read as an
    /// instruction to open their bags, and gear the retainer has on is not in them.
    /// </remarks>
    RetainerEquipped,
}

/// <summary>
/// The ownership decision, kept free of Dalamud and Lumina so it can be reasoned about (and tested)
/// on its own.
/// </summary>
public static class MissingItems
{
    /// <summary>
    /// Where a piece is, taking the first answer that holds.
    /// </summary>
    /// <remarks>
    /// The order is not arbitrary and is the single thing every surface in the plugin depends on
    /// agreeing about. The two glamour stores come first because a piece in one can actually be
    /// worn; the held places follow in order of how much trouble the copy is to get at, so the
    /// answer doubles as advice on where to go for it.
    ///
    /// A null holding means "not counted" rather than "empty", which is how the storage scope and
    /// the count-inventory and count-retainer settings switch a place off without the caller having
    /// to know they exist.
    /// </remarks>
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

        // The "owned but in no glamour box" answers, in order of how much trouble the copy is to get
        // at: the character's own bags first, then a retainer's, and a retainer's back last of all -
        // that one has to come off them before it is anything.
        if (view.Retainers != null && view.Retainers.Contains(itemId))
            return OwnershipSource.Retainer;

        if (view.RetainersWearing != null && view.RetainersWearing.Contains(itemId))
            return OwnershipSource.RetainerEquipped;

        return OwnershipSource.None;
    }

    /// <summary>
    /// How many of the outfit sets listing a piece are stored holding it, out of how many exist.
    /// </summary>
    /// <remarks>
    /// Only interesting once <see cref="Resolve"/> has said <see cref="OwnershipSource.None"/> in
    /// <see cref="OutfitOwnershipMode.AllOutfits"/>: a non-zero first number there is the difference
    /// between "go and get one" and "one is stored, the chosen rule wants the rest", which a flat "not
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

    /// <summary>The answer as a sentence, for chat and for anything with room for one.</summary>
    public static string Describe(OwnershipSource source) => source switch
    {
        OwnershipSource.Dresser => "In your Glamour Dresser",
        OwnershipSource.Outfit => "Part of a stored outfit set",
        OwnershipSource.Armoire => "In your Armoire",
        OwnershipSource.Inventory => "Carried or equipped",
        OwnershipSource.Retainer => "In a retainer's bags",
        OwnershipSource.RetainerEquipped => "Worn by one of your retainers",
        _ => "Not collected",
    };
}
