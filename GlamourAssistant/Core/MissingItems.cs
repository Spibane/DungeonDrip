using System.Collections.Generic;
using System.Linq;
using GlamourAssistant.Game;

namespace GlamourAssistant.Core;

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

    public static string Describe(OwnershipSource source) => source switch
    {
        OwnershipSource.Dresser => "In your Glamour Dresser",
        OwnershipSource.Outfit => "Part of a stored outfit set",
        OwnershipSource.Armoire => "In your Armoire",
        OwnershipSource.Inventory => "Carried or equipped",
        _ => "Not collected",
    };
}
