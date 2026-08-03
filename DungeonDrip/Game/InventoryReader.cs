using System.Collections.Generic;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace DungeonDrip.Game;

/// <summary>One stack of one item, where it is sitting.</summary>
/// <remarks>
/// Not deduplicated: the same piece can be in a bag, in the armoury and on your back at once, and
/// which of those it is changes what can honestly be said about it. Callers collapse it themselves,
/// keeping whichever location suits the advice they are giving.
/// </remarks>
public readonly record struct CarriedStack(
    uint ItemId,
    InventoryType Container,
    short Slot,
    int Quantity,
    bool HighQuality,
    ushort Spiritbond)
{
    /// <summary>
    /// Whether the Glamour Dresser would take this at all, as far as can be told from here.
    /// </summary>
    /// <remarks>
    /// The box refuses a tradeable piece until it is fully spiritbonded, which is the one refusal
    /// reason that is legible from the stack itself. Untradeable gear is exempt, and whether a piece
    /// is tradeable lives on the sheet rather than here - so this is a necessary condition, not a
    /// sufficient one, and callers must word themselves accordingly.
    /// </remarks>
    public bool FullySpiritbonded => Spiritbond >= 10000;

    public bool IsEquipped => InventoryReader.IsEquipped(Container);

    public bool IsArmoury => InventoryReader.IsArmoury(Container);

    public bool IsSaddlebag => InventoryReader.IsSaddlebag(Container);

    public bool IsBag => InventoryReader.IsBag(Container);
}

public static unsafe class InventoryReader
{
    /// <summary>
    /// Everywhere on your person a glamour piece can sit. Retainer bags are deliberately absent -
    /// the client cannot read them without visiting the retainer.
    /// </summary>
    private static readonly InventoryType[] Containers =
    [
        InventoryType.Inventory1, InventoryType.Inventory2,
        InventoryType.Inventory3, InventoryType.Inventory4,
        InventoryType.EquippedItems,
        InventoryType.ArmoryMainHand, InventoryType.ArmoryOffHand,
        InventoryType.ArmoryHead, InventoryType.ArmoryBody, InventoryType.ArmoryHands,
        InventoryType.ArmoryLegs, InventoryType.ArmoryFeets, InventoryType.ArmoryWaist,
        InventoryType.ArmoryEar, InventoryType.ArmoryNeck, InventoryType.ArmoryWrist,
        InventoryType.ArmoryRings,
        InventoryType.SaddleBag1, InventoryType.SaddleBag2,
        InventoryType.PremiumSaddleBag1, InventoryType.PremiumSaddleBag2,
    ];

    /// <summary>
    /// Reads carried/equipped items. Unlike the dresser and armoire this is always available for the
    /// logged-in character, so it is read live rather than cached.
    /// </summary>
    public static HashSet<uint> Read()
    {
        var held = new HashSet<uint>();

        var manager = InventoryManager.Instance();
        if (manager == null)
            return held;

        foreach (var type in Containers)
        {
            var container = manager->GetInventoryContainer(type);
            if (container == null || !container->IsLoaded)
                continue;

            for (var slot = 0; slot < container->Size; slot++)
            {
                var item = container->GetInventorySlot(slot);
                if (item == null || item->ItemId == 0)
                    continue;

                var (itemId, kind) = ItemUtil.GetBaseId(item->ItemId);
                if (kind != ItemKind.EventItem)
                    held.Add(itemId);
            }
        }

        return held;
    }

    /// <summary>
    /// The same containers, read stack by stack rather than collapsed into a set of ids.
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="Read"/> rather than layered under it. That one runs on a one-second
    /// poll and answers a yes/no question, so it allocates a single set; this allocates a record per
    /// stack and runs only when something asks about what you are carrying. Sharing a body would make
    /// the cheap path pay for the detailed one.
    ///
    /// Callable regardless of the count-inventory setting, deliberately. That setting decides whether
    /// carrying a piece makes it <em>collected</em>; the questions this feeds - what is not stored
    /// yet, what is stored already - are about the bags themselves either way.
    /// </remarks>
    public static IReadOnlyList<CarriedStack> ReadDetailed()
    {
        var stacks = new List<CarriedStack>();

        var manager = InventoryManager.Instance();
        if (manager == null)
            return stacks;

        foreach (var type in Containers)
        {
            var container = manager->GetInventoryContainer(type);
            if (container == null || !container->IsLoaded)
                continue;

            for (var slot = 0; slot < container->Size; slot++)
            {
                var item = container->GetInventorySlot(slot);
                if (item == null || item->ItemId == 0)
                    continue;

                // Same normalisation as Read: HQ collapses onto the base id, which is what the
                // dresser stores, and event items are dropped because their offset id space
                // overlaps real gear. The HQ flag is reported separately rather than lost.
                var (itemId, kind) = ItemUtil.GetBaseId(item->ItemId);
                if (kind == ItemKind.EventItem)
                    continue;

                // The client packs spiritbond and collectability into one field. Collectables are
                // not glamour gear and get filtered long before anything reads this, so on the
                // stacks that survive it is always the spiritbond.
                stacks.Add(new CarriedStack(
                    itemId, type, (short)slot, item->Quantity, kind == ItemKind.Hq,
                    item->SpiritbondOrCollectability));
            }
        }

        return stacks;
    }

    /// <summary>
    /// A hash of what is sitting where, for telling "read again" from "changed".
    /// </summary>
    /// <remarks>
    /// The same trick the dresser snapshot uses, and for the same reason: anything watching the
    /// bags is doing so every frame, and rebuilding a list of several hundred rows on a frame where
    /// nothing moved is the cost worth avoiding. One pass, no allocation.
    /// </remarks>
    public static ulong Fingerprint()
    {
        var hash = 1469598103934665603UL;

        var manager = InventoryManager.Instance();
        if (manager == null)
            return hash;

        foreach (var type in Containers)
        {
            var container = manager->GetInventoryContainer(type);
            if (container == null || !container->IsLoaded)
                continue;

            for (var slot = 0; slot < container->Size; slot++)
            {
                var item = container->GetInventorySlot(slot);
                if (item == null || item->ItemId == 0)
                    continue;

                hash = (hash * 1099511628211UL) ^ item->ItemId;
                hash = (hash * 1099511628211UL) ^ (ulong)((int)type << 16 | (ushort)slot);
                hash = (hash * 1099511628211UL) ^ (uint)item->Quantity;
            }
        }

        return hash;
    }

    /// <summary>
    /// Whether the saddlebag can be read from where you are standing.
    /// </summary>
    /// <remarks>
    /// The client only loads it near a summoning bell, so away from one it reads as empty. Anything
    /// listing what you are carrying has to be able to say "not readable here" rather than letting
    /// an unloaded container pass for an empty one.
    /// </remarks>
    public static bool SaddlebagLoaded()
    {
        var manager = InventoryManager.Instance();
        if (manager == null)
            return false;

        var container = manager->GetInventoryContainer(InventoryType.SaddleBag1);
        return container != null && container->IsLoaded;
    }

    /// <summary>Worn right now. Never a candidate for advice about getting rid of something.</summary>
    public static bool IsEquipped(InventoryType type) => type == InventoryType.EquippedItems;

    /// <summary>In the armoury chest, where a gearset may be quietly depending on it.</summary>
    public static bool IsArmoury(InventoryType type) => type
        is InventoryType.ArmoryMainHand or InventoryType.ArmoryOffHand
        or InventoryType.ArmoryHead or InventoryType.ArmoryBody or InventoryType.ArmoryHands
        or InventoryType.ArmoryLegs or InventoryType.ArmoryFeets or InventoryType.ArmoryWaist
        or InventoryType.ArmoryEar or InventoryType.ArmoryNeck or InventoryType.ArmoryWrist
        or InventoryType.ArmoryRings;

    /// <summary>In a saddlebag, which has to be emptied at a bell before anything can be done.</summary>
    public static bool IsSaddlebag(InventoryType type) => type
        is InventoryType.SaddleBag1 or InventoryType.SaddleBag2
        or InventoryType.PremiumSaddleBag1 or InventoryType.PremiumSaddleBag2;

    /// <summary>Loose in your bags, which is the only place anything can be acted on directly.</summary>
    public static bool IsBag(InventoryType type) => type
        is InventoryType.Inventory1 or InventoryType.Inventory2
        or InventoryType.Inventory3 or InventoryType.Inventory4;
}
