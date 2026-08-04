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
/// How far through an outfit set the collection is, under both readings of "have".
/// </summary>
/// <param name="Owned">
/// Pieces the collection holds anywhere, by the same rules every other list obeys.
/// </param>
/// <remarks>
/// Deliberately one count rather than two. This used to also report how many slots were filled in
/// the dresser's own copy of the set, and the two disagreed in ways that read as a mistake: they
/// were out of different totals, and the owned count obeys the storage scope and outfit mode where
/// the filled one cannot. "7 of 9 owned, 8 filled" is two answers to two questions nobody asked
/// separately.
/// </remarks>
public sealed record SetStanding(
    uint SetId,
    string Name,
    ushort IconId,
    int Owned,
    int Total,
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
/// answers "are these pieces held anywhere", which has to obey the storage scope and outfit mode
/// like every other list the plugin draws. Both are shown, because they are different questions
/// with different next actions.
/// </remarks>
public static class SetCompletion
{
    public static IReadOnlyList<SetStanding> InProgress(
        OutfitCatalog outfits, OwnershipView view, Configuration configuration, EquipLockFilter equipLocks)
    {
        var qualifying = new List<(uint SetId, int Owned, int Total)>();

        // First pass counts only. Naming a set costs a sheet lookup per piece, and the great
        // majority of sets have nothing owned from them, so nothing is named until it has earned a
        // place on the list.
        foreach (var setId in outfits.SetIds)
        {
            // Projected only when the filter is on, so the common path keeps handing the catalogue's
            // own set around rather than allocating a copy of it a thousand times over.
            IReadOnlyCollection<uint> pieces = Filtering(configuration)
                ? [.. outfits.PiecesOf(setId).Where(piece => Wearable(piece, configuration, equipLocks))]
                : outfits.PiecesOf(setId);

            // Empty here now means one of two things, and both belong off the list: a set the sheet
            // gives no pieces for, and a set that is nothing but pieces this character cannot wear.
            if (pieces.Count == 0)
                continue;

            var owned = pieces.Count(piece => Resolve(piece, outfits, view, configuration) != OwnershipSource.None);

            // "In progress" excludes both ends on purpose. A finished set has nothing to do about
            // it, and a set with nothing owned from it is just the rest of the catalogue.
            if (owned > 0 && owned < pieces.Count)
                qualifying.Add((setId, owned, pieces.Count));
        }

        // Closest to done first, then fewest pieces outstanding, so the top of the list is what is
        // actually within reach rather than what is merely large.
        return
        [
            .. qualifying
                .Select(entry => Describe(entry.SetId, outfits, view, configuration, equipLocks))

                // Re-applied against the standing's own totals. The pass above counts every piece
                // the set lists; this one counts the pieces that resolved to a real item, and a
                // set can qualify on one and not the other.
                .Where(standing => standing.Owned > 0 && standing.Owned < standing.Total)
                .OrderByDescending(standing => standing.Fraction)
                .ThenBy(standing => standing.Total - standing.Owned)
                .ThenBy(standing => standing.Name, StringComparer.OrdinalIgnoreCase),
        ];
    }

    public static SetStanding Describe(
        uint setId,
        OutfitCatalog outfits,
        OwnershipView view,
        Configuration configuration,
        EquipLockFilter equipLocks)
    {
        var items = Plugin.DataManager.GetExcelSheet<Item>();
        var states = new List<SetPieceState>();

        foreach (var piece in outfits.PiecesOf(setId))
        {
            if (!items.TryGetRow(piece, out var item))
                continue;

            // Left out of the pieces and out of the totals both, so a set that is half unwearable
            // reads as done once the wearable half is collected rather than stalling at 4 of 8
            // against pieces the character can never get.
            if (equipLocks.Hides(item, configuration))
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
            states);
    }

    /// <summary>
    /// Whether a piece belongs in a set's reckoning at all.
    /// </summary>
    /// <remarks>
    /// A sheet read per piece of every set in the game, which the counting pass is otherwise careful
    /// to avoid - but only while the filter is on, and only when the ownership view has moved. The
    /// alternative is a second index over the same data to save a lookup that Lumina already caches.
    /// </remarks>
    private static bool Wearable(uint itemId, Configuration configuration, EquipLockFilter equipLocks)
    {
        if (!Filtering(configuration))
            return true;

        return !Plugin.DataManager.GetExcelSheet<Item>().TryGetRow(itemId, out var item) ||
               !equipLocks.Hides(item, configuration);
    }

    /// <summary>Whether either lock filter is on, and so whether a set's pieces need sifting.</summary>
    private static bool Filtering(Configuration configuration) =>
        configuration.OnlyCurrentGenderEquippable || configuration.OnlyCurrentRaceEquippable;

    private static OwnershipSource Resolve(
        uint itemId, OutfitCatalog outfits, OwnershipView view, Configuration configuration) =>
        MissingItems.Resolve(
            itemId, view, outfits.SetsContaining(itemId),
            configuration.OutfitOwnership, configuration.Scope);
}
