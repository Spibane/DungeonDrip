using System;
using System.Collections.Generic;
using System.Linq;
using Lumina.Excel.Sheets;

namespace DungeonDrip.Game;

/// <summary>
/// Static view of the game's outfit sets: which pieces each set is made of, and which sets a given
/// piece belongs to.
/// </summary>
/// <remarks>
/// A Glamour Dresser slot can hold an outfit set instead of a single item. The set's components live
/// in the MirageStoreSetItem sheet, whose row id is the outfit's own item id. Slot order below is the
/// sheet's column order, which is also the bit order used by
/// <c>MirageManager.IsSetSlotUnlocked</c> and <c>RestorePrismBoxSetItem</c>.
/// </remarks>
public sealed class OutfitCatalog
{
    public const int SlotCount = 11;

    private readonly Dictionary<uint, HashSet<uint>> setsByPiece;
    private readonly Dictionary<uint, HashSet<uint>> piecesBySet;

    private OutfitCatalog(
        Dictionary<uint, HashSet<uint>> setsByPiece,
        Dictionary<uint, HashSet<uint>> piecesBySet)
    {
        this.setsByPiece = setsByPiece;
        this.piecesBySet = piecesBySet;
    }

    /// <summary>Every outfit set that lists <paramref name="itemId"/> as one of its pieces.</summary>
    public IReadOnlySet<uint> SetsContaining(uint itemId) =>
        setsByPiece.TryGetValue(itemId, out var sets) ? sets : EmptySet;

    /// <summary>Every outfit set in the game, for anything that has to sweep them.</summary>
    public IReadOnlyCollection<uint> SetIds => piecesBySet.Keys;

    /// <summary>Every piece the given outfit set is made of.</summary>
    public IReadOnlySet<uint> PiecesOf(uint setId) =>
        piecesBySet.TryGetValue(setId, out var pieces) ? pieces : EmptySet;

    /// <summary>
    /// The pieces of an outfit set, in the sheet's slot order, for anything that has to hand them to
    /// the game one at a time.
    /// </summary>
    /// <remarks>
    /// Read off the sheet rather than out of <see cref="piecesBySet"/>: that one is a set, and a
    /// fitting room filled weapon-first then top-down reads as an outfit being put on, whereas hash
    /// order reads as noise.
    ///
    /// That sheet read is per call, so this is for the handful of sets someone picked out of a
    /// menu. Anything sweeping every set wants <see cref="PiecesOf"/> and its own ordering.
    /// </remarks>
    public IReadOnlyList<uint> PiecesInSlotOrder(uint setId)
    {
        var sheet = Plugin.DataManager.GetExcelSheet<MirageStoreSetItem>();
        if (!sheet.TryGetRow(setId, out var row))
            return [];

        var pieces = new List<uint>(SlotCount);

        for (var slot = 0; slot < SlotCount; slot++)
        {
            var piece = GetSlotItemId(row, slot);
            if (piece != 0)
                pieces.Add(piece);
        }

        return pieces;
    }

    /// <summary>
    /// The outfit sets a piece belongs to, named and sorted, ready to be listed to the user.
    /// </summary>
    public IReadOnlyList<(uint Id, string Name)> NamedSetsContaining(uint itemId)
    {
        var sets = SetsContaining(itemId);
        if (sets.Count == 0)
            return [];

        var items = Plugin.DataManager.GetExcelSheet<Item>();

        return sets
            .Select(setId => (
                Id: setId,
                Name: items.TryGetRow(setId, out var row) ? row.Name.ExtractText() : $"Outfit {setId}"))
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Whether any stored outfit set holding this piece has every one of its slots filled.
    /// </summary>
    /// <remarks>
    /// A set can sit in the dresser with gaps in it, so "finished" is different news from
    /// "accounted for".
    /// </remarks>
    public bool IsInCompletedSet(uint itemId, OwnershipView view)
    {
        if (!view.DresserOutfits.TryGetValue(itemId, out var holders))
            return false;

        foreach (var setId in holders)
        {
            if (IsComplete(setId, view))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether every slot of a stored set is filled.
    /// </summary>
    /// <remarks>
    /// "Complete as stored" is narrower than "are these pieces owned" - a piece can be in the
    /// Armoire, or in a different set, and still leave this slot empty. The broader reading obeys
    /// the storage scope and outfit mode, so it lives with the ownership decision rather than here.
    /// </remarks>
    private bool IsComplete(uint setId, OwnershipView view)
    {
        var pieces = PiecesOf(setId);
        if (pieces.Count == 0)
            return false;

        foreach (var piece in pieces)
        {
            // The reader only records a piece against a set when the slot is unlocked, so
            // membership here answers "is this slot filled".
            if (!view.DresserOutfits.TryGetValue(piece, out var owners) || !owners.Contains(setId))
                return false;
        }

        return true;
    }

    private static readonly HashSet<uint> EmptySet = [];

    public static OutfitCatalog Build()
    {
        var sheet = Plugin.DataManager.GetExcelSheet<MirageStoreSetItem>();
        var byPiece = new Dictionary<uint, HashSet<uint>>();
        var bySet = new Dictionary<uint, HashSet<uint>>();

        foreach (var row in sheet)
        {
            for (var slot = 0; slot < SlotCount; slot++)
            {
                var piece = GetSlotItemId(row, slot);
                if (piece == 0)
                    continue;

                if (!byPiece.TryGetValue(piece, out var sets))
                    byPiece[piece] = sets = [];

                sets.Add(row.RowId);

                if (!bySet.TryGetValue(row.RowId, out var pieces))
                    bySet[row.RowId] = pieces = [];

                pieces.Add(piece);
            }
        }

        return new OutfitCatalog(byPiece, bySet);
    }

    /// <summary>
    /// A set's piece in one slot, zero when the set has none there.
    /// </summary>
    /// <remarks>
    /// The sheet has a named column per slot rather than an array, so an index has to be turned back
    /// into a column. The order is the sheet's own and must stay that way: it is the bit order
    /// <c>MirageManager.IsSetSlotUnlocked</c> and <c>RestorePrismBoxSetItem</c> use, so reordering it
    /// would silently ask the client about the wrong slot. Note there is no waist column.
    /// </remarks>
    public static uint GetSlotItemId(MirageStoreSetItem row, int slot) => slot switch
    {
        0 => row.MainHand.RowId,
        1 => row.OffHand.RowId,
        2 => row.Head.RowId,
        3 => row.Body.RowId,
        4 => row.Hands.RowId,
        5 => row.Legs.RowId,
        6 => row.Feet.RowId,
        7 => row.Earrings.RowId,
        8 => row.Necklace.RowId,
        9 => row.Bracelets.RowId,
        10 => row.Ring.RowId,
        _ => 0,
    };
}
