using System;
using System.Collections.Generic;
using System.Linq;
using DungeonDrip.Game;
using Lumina.Excel.Sheets;

namespace DungeonDrip.Core;

/// <summary>Where a carried piece is sitting, in terms of what you can do with it.</summary>
public enum CarryLocation
{
    /// <summary>Loose in your bags. The only place a piece can be acted on with no preamble.</summary>
    Bags,

    /// <summary>In the armoury chest, where a gearset may quietly be depending on it.</summary>
    Armoury,

    /// <summary>Worn. Has to come off first, and is never a candidate for getting rid of.</summary>
    Equipped,

    /// <summary>In a saddlebag, which has to be emptied at a bell before anything can happen.</summary>
    Saddlebag,
}

/// <summary>One piece you are carrying, collapsed from however many stacks of it there were.</summary>
public sealed record CarriedPiece(
    uint ItemId,
    string Name,
    ushort IconId,
    int SlotOrder,
    string SlotName,
    CarryLocation Location,
    int Quantity,
    OwnershipSource StoredIn,
    StorageKind Storable)
{
    /// <summary>The Armoire would take this and it is not in there yet - a dresser slot saved.</summary>
    public bool ArmoireWouldTake =>
        Storable.HasFlag(StorageKind.Armoire) && StoredIn != OwnershipSource.Armoire;
}

/// <summary>
/// What you are carrying, split by whether the collection already has it.
/// </summary>
/// <param name="AlreadyStored">
/// Carried and also sitting in the Dresser or Armoire. Kept per location, because what you can
/// safely do about it differs by where it is. Never contains anything equipped.
/// </param>
/// <param name="OnlyInsideAnOutfit">
/// Held only as a filled slot inside a stored outfit set. Accounted for, but pulling the set apart
/// would strand it, so this is reported and never recommended.
/// </param>
/// <param name="NotStored">
/// Carried and nowhere in the collection. One row per piece, offered from the easiest place to
/// reach it from.
/// </param>
public sealed record CarriedGearReport(
    IReadOnlyList<CarriedPiece> AlreadyStored,
    IReadOnlyList<CarriedPiece> OnlyInsideAnOutfit,
    IReadOnlyList<CarriedPiece> NotStored,
    bool SaddlebagReadable);

/// <summary>
/// Answers "is what I am carrying already in my collection" - in both directions at once.
/// </summary>
/// <remarks>
/// The two questions are complements over one computation. What is carried but not stored is an
/// add list; what is carried and already stored is a list of things taking up bag space for
/// nothing. Splitting them here rather than computing each separately is the only way the two can
/// be guaranteed not to contradict each other.
/// </remarks>
public static class CarriedGear
{
    public static CarriedGearReport Build(
        IReadOnlyList<CarriedStack> carried,
        OwnershipView ownership,
        OutfitCatalog outfits,
        StorageEligibility storage)
    {
        // The single most important line here. Every piece in `carried` is by definition in the
        // inventory, so leaving the inventory in scope makes each of them own itself and both halves
        // of the answer come out empty.
        var stored = ownership with { Inventory = null };

        var items = Plugin.DataManager.GetExcelSheet<Item>();
        var alreadyStored = new List<CarriedPiece>();
        var insideOutfit = new List<CarriedPiece>();
        var notStored = new Dictionary<uint, CarriedPiece>();

        foreach (var group in Collapse(carried))
        {
            if (!items.TryGetRow(group.ItemId, out var item))
                continue;

            var storable = storage.Of(item);
            if (storable == StorageKind.None)
                continue;

            // Deliberately not the user's Scope or outfit mode. Those settings say what should count
            // as collected when deciding whether to chase a piece; the question here is the flatter
            // one of whether the thing is physically in a box somewhere, and the answer to that does
            // not change because someone narrowed their duty list to the Armoire.
            var source = MissingItems.Resolve(
                group.ItemId, stored, outfits.SetsContaining(group.ItemId),
                OutfitOwnershipMode.AnyOutfit, CollectionScope.Both);

            var (slotOrder, slotName) = EquipSlots.Describe(item.EquipSlotCategory.Value);
            var piece = new CarriedPiece(
                group.ItemId,
                item.Name.ExtractText(),
                item.Icon,
                slotOrder,
                slotName,
                group.Location,
                group.Quantity,
                source,
                storable);

            switch (source)
            {
                case OwnershipSource.None:
                    Offer(notStored, piece);
                    break;

                // Nothing worn is ever a candidate for disposal advice, and the filter is here
                // rather than in the drawing code so no later change can leak it back in.
                case OwnershipSource.Outfit when group.Location != CarryLocation.Equipped:
                    insideOutfit.Add(piece);
                    break;

                case OwnershipSource.Dresser or OwnershipSource.Armoire
                    when group.Location != CarryLocation.Equipped:
                    alreadyStored.Add(piece);
                    break;
            }
        }

        return new CarriedGearReport(
            Sorted(alreadyStored),
            Sorted(insideOutfit),
            Sorted(notStored.Values),
            InventoryReader.SaddlebagLoaded());
    }

    /// <summary>
    /// Stacks to one row per piece per location, summing how many of it you have there.
    /// </summary>
    private static IEnumerable<Collapsed> Collapse(IReadOnlyList<CarriedStack> carried) =>
        carried
            .GroupBy(stack => (stack.ItemId, Location: LocationOf(stack)))
            .Select(group => new Collapsed(
                group.Key.ItemId,
                group.Key.Location,
                group.Sum(stack => stack.Quantity)));

    /// <summary>
    /// Records a piece on the add list, keeping the copy that is least trouble to get at.
    /// </summary>
    /// <remarks>
    /// An add list wants one row per piece, not one per place you happen to have it. Bags first
    /// because nothing has to happen before you can use that copy; the saddlebag last because it
    /// cannot be reached at all without a trip to a bell.
    /// </remarks>
    private static void Offer(Dictionary<uint, CarriedPiece> destination, CarriedPiece piece)
    {
        if (destination.TryGetValue(piece.ItemId, out var existing) &&
            Reachability(existing.Location) <= Reachability(piece.Location))
        {
            return;
        }

        destination[piece.ItemId] = piece;
    }

    private static int Reachability(CarryLocation location) => location switch
    {
        CarryLocation.Bags => 0,
        CarryLocation.Armoury => 1,
        CarryLocation.Equipped => 2,
        _ => 3,
    };

    private static CarryLocation LocationOf(CarriedStack stack)
    {
        if (stack.IsEquipped)
            return CarryLocation.Equipped;

        if (stack.IsArmoury)
            return CarryLocation.Armoury;

        if (stack.IsSaddlebag)
            return CarryLocation.Saddlebag;

        return CarryLocation.Bags;
    }

    private static IReadOnlyList<CarriedPiece> Sorted(IEnumerable<CarriedPiece> pieces) =>
    [
        .. pieces
            .OrderBy(piece => piece.SlotOrder)
            .ThenBy(piece => piece.Name, StringComparer.OrdinalIgnoreCase),
    ];

    private readonly record struct Collapsed(uint ItemId, CarryLocation Location, int Quantity);
}
