using System.Collections.Generic;
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

    /// <summary>Every piece the given outfit set is made of.</summary>
    public IReadOnlySet<uint> PiecesOf(uint setId) =>
        piecesBySet.TryGetValue(setId, out var pieces) ? pieces : EmptySet;

    /// <summary>
    /// Whether any stored outfit set holding this piece has every one of its slots filled.
    /// </summary>
    /// <remarks>
    /// The distinction worth drawing for a completionist: a set can sit in the dresser for a long
    /// time with gaps in it, and knowing a piece belongs to one that is actually finished is a
    /// different piece of news from knowing it is merely accounted for.
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

    private bool IsComplete(uint setId, OwnershipView view)
    {
        var pieces = PiecesOf(setId);
        if (pieces.Count == 0)
            return false;

        foreach (var piece in pieces)
        {
            // The dresser reader only records a piece against a set when that slot is genuinely
            // unlocked, so membership here is the same question as "is this slot filled".
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
