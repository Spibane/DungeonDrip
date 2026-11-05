using System;
using System.Collections.Generic;
using System.Linq;
using DungeonDrip.Game;
using Lumina.Excel.Sheets;

namespace DungeonDrip.Core;

/// <summary>A set whose pieces are all in the dresser separately, one slot each.</summary>
/// <param name="SlotsReclaimed">
/// What storing the set as a set would give back: every piece but the one slot the set itself
/// would occupy.
/// </param>
/// <param name="HoldingOutfitItem">
/// Whether the outfit item this needs is in the inventory. When it is, the advice is actionable;
/// when it is not, it is a conditional.
/// </param>
public sealed record CollapsibleSet(
    uint SetId,
    string Name,
    IReadOnlyList<uint> Pieces,
    int SlotsReclaimed,
    bool HoldingOutfitItem);

/// <summary>A piece taking up a dresser slot that something else could be holding.</summary>
public sealed record DresserResident(uint ItemId, string Name, ushort IconId);

/// <summary>An outfit set stored in the dresser whose every held piece the Armoire accepts.</summary>
/// <param name="Pieces">
/// The pieces the set actually holds - its filled slots, not the sheet's full list - since those are
/// what would go into the Armoire. Never empty: a stored set with no slot filled has nothing to move.
/// </param>
/// <param name="AlreadyInArmoire">
/// Which of <paramref name="Pieces"/> the Armoire already has, carried on the report so the
/// presenter does not rebuild an ownership view per row per frame to ask.
/// </param>
public sealed record ArmoireOutfit(
    uint SetId,
    string Name,
    ushort IconId,
    IReadOnlyList<DresserResident> Pieces,
    IReadOnlySet<uint> AlreadyInArmoire)
{
    /// <summary>The dresser slot is duplicating the Armoire outright.</summary>
    public bool WhollyInArmoire => AlreadyInArmoire.Count == Pieces.Count;
}

/// <param name="Capacity">
/// Zero when there is no snapshot to read it from. Also see the caveat on
/// <see cref="DresserSnapshot.SlotCapacity"/> - it is the box's structural size.
/// </param>
/// <param name="DuplicateInArmoire">
/// In the dresser and in the Armoire both. The dresser copy is pure duplication.
/// </param>
/// <param name="ArmoireWouldTake">
/// In the dresser and eligible for the Armoire, but not deposited there yet. Speculative, so it
/// never counts toward the headline.
/// </param>
/// <param name="OutfitsArmoireWouldTake">
/// Stored outfit sets the Armoire would take a piece at a time. Empty, not unknown, while
/// <paramref name="ArmoireKnown"/> is false - the presenter checks that first.
/// </param>
public sealed record DresserPressureReport(
    int Used,
    int Capacity,
    IReadOnlyList<CollapsibleSet> Collapsible,
    int CollapsibleSlots,
    IReadOnlyList<DresserResident> DuplicateInArmoire,
    IReadOnlyList<DresserResident> ArmoireWouldTake,
    IReadOnlyList<ArmoireOutfit> OutfitsArmoireWouldTake,
    bool ArmoireKnown)
{
    /// <summary>What could be freed without giving anything up. Deliberately conservative.</summary>
    public int Reclaimable => CollapsibleSlots + DuplicateInArmoire.Count;

    public bool HasData => Used > 0 || Capacity > 0;
}

/// <summary>
/// How full the Glamour Dresser is, and what could be done about it.
/// </summary>
/// <remarks>
/// Dresser space is a real constraint and the box gives no help with it, so this is the one place
/// the plugin volunteers a suggestion rather than answering a question. That is worth being careful
/// about: everything here is phrased as an opportunity with its precondition attached, and the
/// headline only counts the two levers that cost nothing.
/// </remarks>
public static class DresserPressure
{
    public static DresserPressureReport Build(
        OwnershipView view, OutfitCatalog outfits, StorageEligibility storage, bool armoireKnown)
    {
        var items = Plugin.DataManager.GetExcelSheet<Item>();
        var collapsible = new List<CollapsibleSet>();

        // Only sets reachable from what is actually in the box. Walking every set in the game to
        // find the handful with pieces held would be thousands of lookups for the same answer.
        var candidates = view.DresserDirect.SelectMany(outfits.SetsContaining).Distinct();

        foreach (var setId in candidates)
        {
            // Already stored as a set - there is nothing left to collapse.
            if (view.StoredOutfits.Contains(setId))
                continue;

            var pieces = outfits.PiecesOf(setId);
            if (pieces.Count < 2 || !pieces.All(view.DresserDirect.Contains))
                continue;

            collapsible.Add(new CollapsibleSet(
                setId,
                items.TryGetRow(setId, out var set) ? set.Name.ExtractText() : $"Outfit {setId}",
                [.. pieces],
                pieces.Count - 1,
                view.Inventory?.Contains(setId) == true));
        }

        // The ones that can be acted on today first.
        var ordered = collapsible
            .OrderByDescending(entry => entry.HoldingOutfitItem)
            .ThenByDescending(entry => entry.SlotsReclaimed)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var duplicate = armoireKnown ? Residents(view.DresserDirect.Where(view.Armoire.Contains)) : [];
        var wouldTake = armoireKnown
            ? Residents(view.DresserDirect.Where(id =>
                !view.Armoire.Contains(id) && storage.Of(id).HasFlag(StorageKind.Armoire)))
            : [];

        var outfitsForArmoire = armoireKnown ? ArmoireOutfits(view, storage) : [];

        return new DresserPressureReport(
            view.Space?.Used ?? 0,
            view.Space?.Capacity ?? 0,
            ordered,
            ordered.Sum(entry => entry.SlotsReclaimed),
            duplicate,
            wouldTake,
            outfitsForArmoire,
            armoireKnown);
    }

    /// <summary>
    /// Stored outfit sets that could leave the dresser for the Armoire, wholly duplicated ones first.
    /// </summary>
    /// <remarks>
    /// The Armoire takes no outfit item: of the 1,089 sets in the sheet, the Cabinet sheet lists none
    /// of the set items themselves. What it does list is pieces, and 469 sets are made entirely of
    /// them - so the move is the set's pieces going in one at a time, and the dresser slot the set held
    /// coming back.
    ///
    /// Tested against the pieces the set holds rather than every piece the sheet gives it. An empty
    /// slot has nothing to move, so it cannot be what stops the move. When this was written no set
    /// was partly eligible either, so the two tests agree; the held one is the one that stays true
    /// if a patch changes that.
    ///
    /// The lookup is inverted here rather than kept inverted on the view, because the view answers
    /// "which sets hold this piece" for every surface and this is the one place asking the reverse.
    /// It runs once per snapshot rebuild, over a few hundred stored pieces at most.
    ///
    /// Never counted toward <see cref="DresserPressureReport.Reclaimable"/>, even when every piece is
    /// already in the Armoire. That headline is for what can be freed without giving anything up, and
    /// a set kept as a set is a thing given up - the Armoire hands its pieces back one at a time, not
    /// as an outfit.
    /// </remarks>
    private static IReadOnlyList<ArmoireOutfit> ArmoireOutfits(
        OwnershipView view, StorageEligibility storage)
    {
        var held = new Dictionary<uint, List<uint>>();

        foreach (var (piece, sets) in view.DresserOutfits)
        {
            foreach (var setId in sets)
            {
                if (!held.TryGetValue(setId, out var pieces))
                    held[setId] = pieces = [];

                pieces.Add(piece);
            }
        }

        var items = Plugin.DataManager.GetExcelSheet<Item>();
        var found = new List<ArmoireOutfit>();

        foreach (var setId in view.StoredOutfits)
        {
            if (!held.TryGetValue(setId, out var pieces))
                continue;

            if (!pieces.All(id => storage.Of(id).HasFlag(StorageKind.Armoire)))
                continue;

            var residents = Residents(pieces);
            var named = items.TryGetRow(setId, out var set);

            found.Add(new ArmoireOutfit(
                setId,
                named ? set.Name.ExtractText() : $"Outfit {setId}",
                named ? set.Icon : (ushort)0,
                residents,
                pieces.Where(view.Armoire.Contains).ToHashSet()));
        }

        return
        [
            .. found
                .OrderByDescending(outfit => outfit.WhollyInArmoire)
                .ThenBy(outfit => outfit.Name, StringComparer.OrdinalIgnoreCase),
        ];
    }

    private static IReadOnlyList<DresserResident> Residents(IEnumerable<uint> itemIds)
    {
        var items = Plugin.DataManager.GetExcelSheet<Item>();

        return
        [
            .. itemIds
                .Select(id => items.TryGetRow(id, out var item)
                    ? new DresserResident(id, item.Name.ExtractText(), item.Icon)
                    : null)
                .Where(resident => resident != null)
                .Select(resident => resident!)
                .OrderBy(resident => resident.Name, StringComparer.OrdinalIgnoreCase),
        ];
    }
}
