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
/// Whether the outfit item this needs is in your inventory. When it is, the advice is actionable;
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
public sealed record DresserPressureReport(
    int Used,
    int Capacity,
    IReadOnlyList<CollapsibleSet> Collapsible,
    int CollapsibleSlots,
    IReadOnlyList<DresserResident> DuplicateInArmoire,
    IReadOnlyList<DresserResident> ArmoireWouldTake,
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
        // find the handful you hold pieces of would be thousands of lookups for the same answer.
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

        // The ones you can act on today first.
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

        return new DresserPressureReport(
            view.Space?.Used ?? 0,
            view.Space?.Capacity ?? 0,
            ordered,
            ordered.Sum(entry => entry.SlotsReclaimed),
            duplicate,
            wouldTake,
            armoireKnown);
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
