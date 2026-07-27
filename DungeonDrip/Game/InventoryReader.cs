using System.Collections.Generic;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace DungeonDrip.Game;

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
}
