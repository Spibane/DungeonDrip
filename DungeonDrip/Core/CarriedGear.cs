using System;
using System.Collections.Generic;
using System.Linq;
using DungeonDrip.Game;
using Lumina.Excel.Sheets;

namespace DungeonDrip.Core;

/// <summary>Where a held piece is sitting, in terms of what can be done with it.</summary>
public enum CarryLocation
{
    /// <summary>Loose in the bags. The only place a piece can be acted on with no preamble.</summary>
    Bags,

    /// <summary>In the armoury chest, where a gearset may quietly be depending on it.</summary>
    Armoury,

    /// <summary>Worn. Has to come off first, and is never a candidate for getting rid of.</summary>
    Equipped,

    /// <summary>In a saddlebag, which has to be emptied at a bell before anything can happen.</summary>
    Saddlebag,

    /// <summary>
    /// In a retainer's bags, which means a trip to them before it is anything at all.
    /// </summary>
    /// <remarks>
    /// A holding rather than a store, which is the whole reason it is in this enum: a piece with a
    /// retainer is no more wearable as a glamour than one in the bags, so it is something not yet put
    /// away rather than somewhere it has been put.
    /// </remarks>
    Retainer,

    /// <summary>
    /// Worn by a retainer, which is not in their bags and is not found by looking there.
    /// </summary>
    /// <remarks>
    /// Apart from <see cref="Retainer"/> because a list that lumps them together names the wrong place,
    /// and because the two want different advice: gear a retainer has on is deciding what their ventures
    /// bring back, so it is the one holding on this list that is doing a job where it is.
    /// </remarks>
    RetainerEquipped,
}

/// <summary>One held piece, collapsed from however many stacks of it there were.</summary>
/// <param name="Quantity">
/// How many are there. Always 1 for a retainer, whose snapshot records ids and no counts - which
/// is safe rather than a fib, because a count is only ever drawn when it is above one.
/// </param>
/// <param name="Holder">
/// Which retainer is holding it, empty for anything on the character. What is worth doing about a
/// spare copy depends on which trip it is on the other end of.
/// </param>
/// <param name="InArmoire">
/// Whether the Armoire has one, asked of the Armoire directly rather than read off
/// <paramref name="StoredIn"/>. That one takes the first answer that holds and the Dresser is ahead
/// of the Armoire in it, so a piece in both boxes reports as the Dresser's - which is the right
/// answer to "where is it" and the wrong one to "does the Armoire have it".
/// </param>
public sealed record CarriedPiece(
    uint ItemId,
    string Name,
    ushort IconId,
    int SlotOrder,
    string SlotName,
    CarryLocation Location,
    int Quantity,
    OwnershipSource StoredIn,
    StorageKind Storable,
    bool InArmoire,
    string Holder = "")
{
    /// <summary>The Armoire would take this and it is not in there yet - a dresser slot saved.</summary>
    public bool ArmoireWouldTake => Storable.HasFlag(StorageKind.Armoire) && !InArmoire;
}

/// <summary>
/// Everything merely held, split by whether the collection already has it.
/// </summary>
/// <param name="AlreadyStored">
/// Held and also sitting in the Dresser or Armoire. Kept per location, because what is safe to do
/// about it differs by where it is. Never contains anything equipped.
/// </param>
/// <param name="OnlyInsideAnOutfit">
/// Held only as a filled slot inside a stored outfit set - so the dresser is holding a set rather than
/// this piece, and the piece goes with the set if it ever leaves. Reported, never recommended.
/// </param>
/// <param name="NotStored">
/// Held and nowhere in the collection. One row per piece, offered from the easiest place to reach
/// it from.
/// </param>
/// <param name="ArmoireCandidates">
/// Held pieces the Armoire accepts and does not already have. Not derivable from the three lists
/// above, which is why it is its own: two of them leave out anything equipped, and a piece sitting in
/// the Dresser is in one of them while still being something the Armoire has never seen. One row per
/// piece, from the easiest copy to reach, as <paramref name="NotStored"/> is.
/// </param>
/// <param name="ArmoireDuplicates">
/// How many held, Armoire-eligible pieces the Armoire already has - which is the count the game's own
/// "store an item" screen will not give: it lists what the Armoire can take without saying which of
/// those it is already holding, and refuses only on the attempt.
/// </param>
public sealed record CarriedGearReport(
    IReadOnlyList<CarriedPiece> AlreadyStored,
    IReadOnlyList<CarriedPiece> OnlyInsideAnOutfit,
    IReadOnlyList<CarriedPiece> NotStored,
    IReadOnlyList<CarriedPiece> ArmoireCandidates,
    int ArmoireDuplicates,
    bool SaddlebagReadable);

/// <summary>
/// Answers "is what I am only holding already in my collection" - in both directions at once.
/// </summary>
/// <remarks>
/// The two questions are complements over one computation. What is held but not stored is an add
/// list; what is held and already stored is a list of things taking up space for nothing. Splitting
/// them here rather than computing each separately is the only way the two can be guaranteed not to
/// contradict each other.
///
/// Retainers are holdings on the same footing as the bags, which is why they arrive here rather than
/// being treated as another box to compare against. A piece with a retainer cannot be worn as a
/// glamour any more than one in a bag can; it is owned and not put away.
/// </remarks>
public static class CarriedGear
{
    /// <param name="retainers">
    /// The retainers whose bags have been read, which is allowed to be empty - and is, for the panel
    /// beside the dresser, whose subject is what can be acted on without going anywhere.
    /// </param>
    /// <param name="outfitMode">
    /// The user's rule for a piece a stored outfit set is holding.
    /// </param>
    public static CarriedGearReport Build(
        IReadOnlyList<CarriedStack> carried,
        IReadOnlyList<RetainerHolding> retainers,
        OwnershipView ownership,
        OutfitCatalog outfits,
        StorageEligibility storage,
        OutfitOwnershipMode outfitMode)
    {
        // The single most important line here. Every piece being considered is by definition somewhere
        // in the inventory or with a retainer, so leaving any of those in scope makes each of them own
        // itself and both halves of the answer come out empty.
        var stored = ownership with { Inventory = null, Retainers = null, RetainersWearing = null };

        var items = Plugin.DataManager.GetExcelSheet<Item>();
        var alreadyStored = new List<CarriedPiece>();
        var insideOutfit = new List<CarriedPiece>();
        var notStored = new Dictionary<uint, CarriedPiece>();
        var armoireCandidates = new Dictionary<uint, CarriedPiece>();

        // Per piece rather than per stack, so two of a thing in two bags is one duplicate and not two.
        var armoireDuplicates = new HashSet<uint>();

        foreach (var group in Collapse(carried, retainers))
        {
            if (!items.TryGetRow(group.ItemId, out var item))
                continue;

            var storable = storage.Of(item);
            if (storable == StorageKind.None)
                continue;

            // The outfit mode is the user's, and the scope deliberately is not. Both look like the
            // same kind of setting and are not: the scope narrows which box is being asked about, and
            // this question is about every box - a piece in the Dresser is in a box however narrowly
            // the duty list is looking. The outfit mode is a rule about what counts as having a piece
            // at all, and the add list is the list that rule exists to change. Under "owned only if
            // every outfit with that piece is stored", a piece one of its two sets is holding is one
            // the user has said they do not have, so it belongs on the list of things to put in - and
            // it is the same piece every other surface is already marking as not collected.
            var source = MissingItems.Resolve(
                group.ItemId, stored, outfits.SetsContaining(group.ItemId),
                outfitMode, CollectionScope.Both);

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
                storable,
                stored.Armoire.Contains(group.ItemId),
                group.Holder);

            // Answered off to one side of the switch below, because it is a different question and
            // cuts across every one of its cases: the Armoire either has the piece or it does not,
            // whatever the Dresser is doing about it and whether or not it is being worn.
            if (piece.ArmoireWouldTake)
                Offer(armoireCandidates, piece);
            else if (piece.InArmoire)
                armoireDuplicates.Add(piece.ItemId);

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
            Sorted(armoireCandidates.Values),
            armoireDuplicates.Count,
            InventoryReader.SaddlebagLoaded());
    }

    /// <summary>
    /// Stacks to one row per piece per place, summing how many of it are there.
    /// </summary>
    /// <remarks>
    /// Retainers come through as one row each rather than being merged into a single retainer bucket,
    /// because "which one has it" is the only thing that makes a spare copy actionable.
    /// </remarks>
    private static IEnumerable<Collapsed> Collapse(
        IReadOnlyList<CarriedStack> carried, IReadOnlyList<RetainerHolding> retainers)
    {
        var onYou = carried
            .GroupBy(stack => (stack.ItemId, Location: LocationOf(stack)))
            .Select(group => new Collapsed(
                group.Key.ItemId,
                group.Key.Location,
                group.Sum(stack => stack.Quantity),
                string.Empty));

        var inRetainerBags = retainers.SelectMany(retainer => retainer.Items.Select(
            itemId => new Collapsed(itemId, CarryLocation.Retainer, 1, retainer.Name)));

        var wornByRetainers = retainers.SelectMany(retainer => retainer.Equipped.Select(
            itemId => new Collapsed(itemId, CarryLocation.RetainerEquipped, 1, retainer.Name)));

        return onYou.Concat(inRetainerBags).Concat(wornByRetainers);
    }

    /// <summary>
    /// Records a piece on the add list, keeping the copy that is least trouble to get at.
    /// </summary>
    /// <remarks>
    /// An add list wants one row per piece, not one per place it happens to sit. Bags first because
    /// nothing has to happen before that copy can be used; then the saddlebag, which needs a
    /// trip to a bell, and a retainer last, which needs the trip and a retainer opened at the end of it.
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
        CarryLocation.Saddlebag => 3,
        CarryLocation.Retainer => 4,

        // Last, because it has to come off the retainer before it is even in their bags.
        _ => 5,
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

    /// <summary>One piece in one place, with however many of it are there summed.</summary>
    private readonly record struct Collapsed(
        uint ItemId, CarryLocation Location, int Quantity, string Holder);
}
