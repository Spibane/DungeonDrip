using System.Collections.Generic;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace DungeonDrip.Game;

/// <summary>What one retainer was holding the last time they were open.</summary>
public sealed class RetainerSnapshot
{
    /// <summary>The client's own id for this retainer, which survives a rename.</summary>
    public ulong RetainerId { get; init; }

    public string Name { get; init; } = string.Empty;

    /// <summary>In the retainer's seven bag pages, where it can be gone to and taken out.</summary>
    public HashSet<uint> Items { get; init; } = [];

    /// <summary>
    /// On the retainer, worn.
    /// </summary>
    /// <remarks>
    /// Kept apart from the bags rather than merged in, which is how this started and was wrong in a way
    /// that costs real time: "with Ysayle" is read as an instruction to go to Ysayle and search seven
    /// pages of bags for something she is wearing. The two are different answers and only one of them is
    /// a place to look.
    /// </remarks>
    public HashSet<uint> Equipped { get; init; } = [];

    /// <summary>
    /// Hash of the bags this was read from, for telling "read again" from "changed".
    /// </summary>
    /// <remarks>
    /// The same trick the dresser snapshot uses and for the same reason: the bags stay loaded for as
    /// long as the retainer is open, so they are re-read on every poll, and without something to
    /// compare each of those reads would rewrite the cache file. Zero for a snapshot restored from
    /// disk, which is what makes the first live read save.
    /// </remarks>
    public ulong Fingerprint { get; init; }
}

/// <summary>
/// Reads the retainer currently open.
/// </summary>
/// <remarks>
/// The client only loads a retainer's bags while standing at that retainer with them open, and drops
/// them again afterwards - so like the dresser and the Armoire this is snapshotted whenever the data
/// happens to be there and answered from the cache the rest of the time. Unlike those two it is per
/// retainer rather than per character, so the cache holds one entry each and a retainer that has never
/// been opened is simply absent rather than empty.
///
/// <para><b>The market is deliberately not read.</b> Gear listed for sale has been given up on, and it
/// can leave without the retainer ever being visited again - so a cached snapshot of it would go wrong
/// in the one direction that matters, calling off the hunt for a piece that is no longer owned. The
/// seven bag pages and whatever the retainer is wearing are things that stay put.</para>
///
/// <para>Those two are reported separately. What a retainer is wearing is not in their bags, and saying
/// otherwise sends the reader through seven pages after a piece that was never going to be in any of
/// them.</para>
/// </remarks>
public static unsafe class RetainerReader
{
    /// <summary>The bags: seven pages that can be opened and taken from.</summary>
    private static readonly InventoryType[] BagContainers =
    [
        InventoryType.RetainerPage1, InventoryType.RetainerPage2,
        InventoryType.RetainerPage3, InventoryType.RetainerPage4,
        InventoryType.RetainerPage5, InventoryType.RetainerPage6,
        InventoryType.RetainerPage7,
    ];

    /// <returns><c>null</c> unless a retainer's bags are loaded right now.</returns>
    /// <remarks>
    /// Both halves of the guard matter. The active retainer is what names the snapshot, and without
    /// it there would be nothing to file the contents under; the loaded check is what keeps an
    /// unloaded container from passing for an empty retainer and wiping a good snapshot. A retainer
    /// who genuinely holds nothing still records, because "read, and empty" is an answer.
    /// </remarks>
    public static RetainerSnapshot? TryRead()
    {
        var manager = RetainerManager.Instance();
        if (manager == null || !manager->IsReady)
            return null;

        var retainer = manager->GetActiveRetainer();
        if (retainer == null || retainer->RetainerId == 0)
            return null;

        var inventory = InventoryManager.Instance();
        if (inventory == null)
            return null;

        var items = new HashSet<uint>();
        var equipped = new HashSet<uint>();
        var fingerprint = 1469598103934665603UL;
        var loaded = false;

        foreach (var type in BagContainers)
            loaded |= Gather(inventory, type, items, ref fingerprint);

        // Not part of the loaded check. A retainer wearing nothing at all is normal, and the bags are
        // what says whether this retainer's data is really here.
        Gather(inventory, InventoryType.RetainerEquippedItems, equipped, ref fingerprint);

        if (!loaded)
            return null;

        return new RetainerSnapshot
        {
            RetainerId = retainer->RetainerId,
            Name = retainer->NameString,
            Items = items,
            Equipped = equipped,
            Fingerprint = fingerprint,
        };
    }

    /// <summary>
    /// Adds one container's contents to a set, folding it into the fingerprint on the way.
    /// </summary>
    /// <returns>Whether the container was loaded and therefore says anything at all.</returns>
    private static bool Gather(
        InventoryManager* inventory, InventoryType type, HashSet<uint> into, ref ulong fingerprint)
    {
        var container = inventory->GetInventoryContainer(type);
        if (container == null || !container->IsLoaded)
            return false;

        for (var slot = 0; slot < container->Size; slot++)
        {
            var item = container->GetInventorySlot(slot);
            if (item == null || item->ItemId == 0)
                continue;

            fingerprint = (fingerprint * 1099511628211UL) ^ item->ItemId;
            fingerprint = (fingerprint * 1099511628211UL) ^ (ulong)((int)type << 16 | (ushort)slot);

            // The same normalisation the bags and the dresser use: HQ collapses onto the base id, and
            // event items are dropped because their offset id space overlaps real gear.
            var (itemId, kind) = ItemUtil.GetBaseId(item->ItemId);
            if (kind != ItemKind.EventItem)
                into.Add(itemId);
        }

        return true;
    }
}
