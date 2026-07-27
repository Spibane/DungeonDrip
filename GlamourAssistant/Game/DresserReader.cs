using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace GlamourAssistant.Game;

/// <summary>What the Glamour Dresser held the last time the client had it loaded.</summary>
public sealed class DresserSnapshot
{
    /// <summary>Pieces stored in their own dresser slot.</summary>
    public HashSet<uint> DirectItems { get; init; } = [];

    /// <summary>Piece -> the stored outfit sets that currently hold it in a filled slot.</summary>
    public Dictionary<uint, HashSet<uint>> ItemsInStoredOutfits { get; init; } = [];

    public int SlotsUsed { get; init; }
}

public static unsafe class DresserReader
{
    /// <summary>
    /// Reads the Glamour Dresser, expanding stored outfit sets into their component pieces.
    /// </summary>
    /// <returns><c>null</c> when the client has no dresser data loaded right now.</returns>
    /// <remarks>
    /// MirageManager clears this data on every zone change and only repopulates it after you
    /// interact with a dresser, which is why the caller caches the result to disk.
    /// </remarks>
    public static DresserSnapshot? TryRead()
    {
        var mirage = MirageManager.Instance();
        if (mirage == null || !mirage->PrismBoxLoaded)
            return null;

        var setSheet = Plugin.DataManager.GetExcelSheet<MirageStoreSetItem>();
        var direct = new HashSet<uint>();
        var viaOutfits = new Dictionary<uint, HashSet<uint>>();
        var used = 0;

        var boxItems = mirage->PrismBoxItemIds;
        for (var index = 0; index < boxItems.Length; index++)
        {
            var rawId = boxItems[index];
            if (rawId == 0)
                continue;

            used++;
            var itemId = ItemId.Normalize(rawId);

            if (!setSheet.TryGetRow(itemId, out var set))
            {
                direct.Add(itemId);
                continue;
            }

            // An outfit set. Slots can be individually empty, so ask the client which are filled
            // rather than trusting the sheet.
            for (var slot = 0; slot < OutfitCatalog.SlotCount; slot++)
            {
                if (!mirage->IsSetSlotUnlocked((uint)index, slot))
                    continue;

                var piece = OutfitCatalog.GetSlotItemId(set, slot);
                if (piece == 0)
                    continue;

                if (!viaOutfits.TryGetValue(piece, out var owners))
                    viaOutfits[piece] = owners = [];

                owners.Add(itemId);
            }
        }

        return new DresserSnapshot
        {
            DirectItems = direct,
            ItemsInStoredOutfits = viaOutfits,
            SlotsUsed = used,
        };
    }
}
