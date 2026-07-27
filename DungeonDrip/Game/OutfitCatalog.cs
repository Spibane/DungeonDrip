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

    private OutfitCatalog(Dictionary<uint, HashSet<uint>> setsByPiece) => this.setsByPiece = setsByPiece;

    /// <summary>Every outfit set that lists <paramref name="itemId"/> as one of its pieces.</summary>
    public IReadOnlySet<uint> SetsContaining(uint itemId) =>
        setsByPiece.TryGetValue(itemId, out var sets) ? sets : EmptySet;

    private static readonly HashSet<uint> EmptySet = [];

    public static OutfitCatalog Build()
    {
        var sheet = Plugin.DataManager.GetExcelSheet<MirageStoreSetItem>();
        var map = new Dictionary<uint, HashSet<uint>>();

        foreach (var row in sheet)
        {
            for (var slot = 0; slot < SlotCount; slot++)
            {
                var piece = GetSlotItemId(row, slot);
                if (piece == 0)
                    continue;

                if (!map.TryGetValue(piece, out var sets))
                    map[piece] = sets = [];

                sets.Add(row.RowId);
            }
        }

        return new OutfitCatalog(map);
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
