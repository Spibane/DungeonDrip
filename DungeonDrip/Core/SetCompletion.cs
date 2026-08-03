using System;
using System.Collections.Generic;
using System.Linq;
using DungeonDrip.Game;
using Lumina.Excel.Sheets;

namespace DungeonDrip.Core;

/// <summary>One piece of a set, and whether the collection has it.</summary>
public sealed record SetPieceState(
    uint ItemId, string Name, ushort IconId, int SlotOrder, string SlotName, OwnershipSource Source)
{
    public bool IsOwned => Source != OwnershipSource.None;
}

/// <summary>
/// How far through an outfit set you are, under both readings of "have".
/// </summary>
/// <param name="Owned">
/// Pieces the collection holds anywhere, by the same rules every other list obeys.
/// </param>
/// <param name="FilledInStoredSet">
/// Slots filled in the copy of this set sitting in the dresser. Only meaningful when
/// <paramref name="StoredAsSet"/> is true.
/// </param>
public sealed record SetStanding(
    uint SetId,
    string Name,
    ushort IconId,
    int Owned,
    int Total,
    int FilledInStoredSet,
    bool StoredAsSet,
    IReadOnlyList<SetPieceState> Pieces)
{
    public IEnumerable<SetPieceState> Missing => Pieces.Where(piece => !piece.IsOwned);

    public float Fraction => Total == 0 ? 0f : (float)Owned / Total;
}

/// <summary>
/// Ranks outfit sets by how close to finished they are.
/// </summary>
/// <remarks>
/// This is the broader reading of "complete" that the outfit catalogue deliberately does not carry.
/// The catalogue answers "is this set stored with every slot filled", which is about the box; this
/// answers "do you have these pieces anywhere", which has to obey the storage scope and outfit mode
/// like every other list the plugin draws. Both are shown, because they are different questions
/// with different next actions.
/// </remarks>
public static class SetCompletion
{
    public static IReadOnlyList<SetStanding> InProgress(
        OutfitCatalog outfits, OwnershipView view, Configuration configuration)
    {
        var qualifying = new List<(uint SetId, int Owned, int Total)>();

        // First pass counts only. Naming a set costs a sheet lookup per piece, and the great
        // majority of sets are ones you own nothing from, so nothing is named until it has earned
        // a place on the list.
        foreach (var setId in outfits.SetIds)
        {
            var pieces = outfits.PiecesOf(setId);
            if (pieces.Count == 0)
                continue;

            var owned = pieces.Count(piece => Resolve(piece, outfits, view, configuration) != OwnershipSource.None);

            // "In progress" excludes both ends on purpose. A finished set has nothing to do about
            // it, and a set you own nothing from is just the rest of the catalogue.
            if (owned > 0 && owned < pieces.Count)
                qualifying.Add((setId, owned, pieces.Count));
        }

        // Closest to done first, then fewest pieces outstanding, so the top of the list is what is
        // actually within reach rather than what is merely large.
        return
        [
            .. qualifying
                .Select(entry => Describe(entry.SetId, outfits, view, configuration))
                .OrderByDescending(standing => standing.Fraction)
                .ThenBy(standing => standing.Total - standing.Owned)
                .ThenBy(standing => standing.Name, StringComparer.OrdinalIgnoreCase),
        ];
    }

    public static SetStanding Describe(
        uint setId, OutfitCatalog outfits, OwnershipView view, Configuration configuration)
    {
        var items = Plugin.DataManager.GetExcelSheet<Item>();
        var progress = outfits.ProgressInDresser(setId, view);
        var states = new List<SetPieceState>();

        foreach (var piece in outfits.PiecesOf(setId))
        {
            if (!items.TryGetRow(piece, out var item))
                continue;

            // Slot order comes off the row already being read for the name. The catalogue's own
            // slot order is the sheet's column order, which has no waist column and so does not
            // line up with the ordering every other list here uses.
            var (slotOrder, slotName) = EquipSlots.Describe(item.EquipSlotCategory.Value);

            states.Add(new SetPieceState(
                piece,
                item.Name.ExtractText(),
                item.Icon,
                slotOrder,
                slotName,
                Resolve(piece, outfits, view, configuration)));
        }

        states.Sort((a, b) =>
        {
            var bySlot = a.SlotOrder.CompareTo(b.SlotOrder);
            return bySlot != 0 ? bySlot : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        var named = items.TryGetRow(setId, out var set);

        return new SetStanding(
            setId,
            named ? set.Name.ExtractText() : $"Outfit {setId}",
            named ? set.Icon : (ushort)0,
            states.Count(state => state.IsOwned),
            states.Count,
            progress.Filled,
            progress.StoredAsSet,
            states);
    }

    private static OwnershipSource Resolve(
        uint itemId, OutfitCatalog outfits, OwnershipView view, Configuration configuration) =>
        MissingItems.Resolve(
            itemId, view, outfits.SetsContaining(itemId),
            configuration.OutfitOwnership, configuration.Scope);
}
