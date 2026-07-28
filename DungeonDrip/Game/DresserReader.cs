using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;
using Dalamud.Utility;
using Lumina.Excel.Sheets;

namespace DungeonDrip.Game;

/// <summary>What the Glamour Dresser held the last time the client had it loaded.</summary>
public sealed class DresserSnapshot
{
    /// <summary>Pieces stored in their own dresser slot.</summary>
    public HashSet<uint> DirectItems { get; init; } = [];

    /// <summary>Piece -> the stored outfit sets that currently hold it in a filled slot.</summary>
    public Dictionary<uint, HashSet<uint>> ItemsInStoredOutfits { get; init; } = [];

    /// <summary>
    /// Every outfit set sitting in the box, whether or not its slots are filled. Lets the UI say
    /// "you have that outfit but this piece is missing from it" rather than just "not stored".
    /// </summary>
    public HashSet<uint> StoredOutfits { get; init; } = [];

    public int SlotsUsed { get; init; }

    /// <summary>
    /// Hash of the raw box this was read from, for telling "read again" from "changed".
    /// </summary>
    /// <remarks>
    /// The box stays loaded until you zone, so it is re-read every second from the moment you open a
    /// dresser. Without something to compare, each of those reads looked like news and rewrote the
    /// cache file. Taken over the raw ids rather than the derived sets because it is the input: equal
    /// input, equal output, and it costs one multiply-add per slot on a read that was happening
    /// anyway. Zero for a snapshot restored from disk, which is what makes the first live read save.
    /// </remarks>
    public ulong Fingerprint { get; init; }
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
        var storedOutfits = new HashSet<uint>();
        var used = 0;
        var fingerprint = 1469598103934665603UL;

        var boxItems = mirage->PrismBoxItemIds;
        for (var index = 0; index < boxItems.Length; index++)
        {
            var rawId = boxItems[index];
            if (rawId == 0)
                continue;

            used++;
            fingerprint = (fingerprint * 1099511628211UL) ^ ((ulong)index << 32 | rawId);
            // Event items share the id space behind an offset; without the kind check their base
            // id collides with real gear.
            var (itemId, kind) = ItemUtil.GetBaseId(rawId);
            if (kind == ItemKind.EventItem)
                continue;

            if (!setSheet.TryGetRow(itemId, out var set))
            {
                direct.Add(itemId);
                continue;
            }

            storedOutfits.Add(itemId);

            // An outfit set. Slots can be individually empty, so ask the client which are filled
            // rather than trusting the sheet.
            for (var slot = 0; slot < OutfitCatalog.SlotCount; slot++)
            {
                if (!mirage->IsSetSlotUnlocked((uint)index, slot))
                    continue;

                // Which slots of a set are filled is content too - topping one up must register.
                fingerprint = (fingerprint * 1099511628211UL) ^ (uint)slot;

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
            StoredOutfits = storedOutfits,
            SlotsUsed = used,
            Fingerprint = fingerprint,
        };
    }
}
