using System;
using System.Collections.Generic;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace DungeonDrip.Game;

/// <summary>
/// Pulls the item ids a vendor is currently offering out of the client.
/// </summary>
/// <remarks>
/// Every path here returns null rather than an empty array when it cannot trust what it read, and
/// an empty array only when the vendor genuinely has nothing to show. The distinction matters: a
/// wrong marker beside a piece of gear is worse than no panel at all, so "I could not read this"
/// has to be able to switch the feature off rather than quietly look like "nothing here".
/// </remarks>
public static unsafe class ShopItemReader
{
    /// <summary>Rows are read into a caller-owned list so the per-frame path allocates nothing.</summary>
    /// <returns>False when the stock could not be read and the panel must not draw.</returns>
    public static bool TryRead(ShopAddonDescriptor descriptor, AtkUnitBase* unit, List<uint> destination)
    {
        destination.Clear();

        return descriptor.Source switch
        {
            ShopSource.ShopEventHandler => ReadShopEventHandler(destination),
            ShopSource.InclusionShop => ReadInclusionShop(unit, destination),
            _ => ReadRawAtkValues(descriptor, unit, destination),
        };
    }

    /// <summary>
    /// The gil shop. The only source with a maintained typed struct behind it, so it fails by
    /// returning a null pointer rather than by handing back plausible nonsense.
    /// </summary>
    private static bool ReadShopEventHandler(List<uint> destination)
    {
        var proxy = ShopEventHandler.AgentProxy.Instance();
        if (proxy == null || proxy->Handler == null)
            return false;

        var handler = proxy->Handler;

        // Buyback lists what you just sold, not what the vendor stocks. Everything in it is by
        // definition something you owned moments ago, so marking it says nothing useful.
        if (handler->BuybackTabActive)
            return true;

        var items = handler->Items;
        var visible = handler->VisibleItems;
        var itemCount = Math.Clamp(handler->ItemsCount, 0, items.Length);
        var visibleCount = Math.Clamp(handler->VisibleItemsCount, 0, visible.Length);

        // VisibleItems holds indices into Items, in the order the game is displaying them - already
        // filtered by unlock state and quest requirements, which is exactly what should be listed.
        for (var i = 0; i < visibleCount; i++)
        {
            var index = visible[i];
            if (index < 0 || index >= itemCount)
                continue;

            Append(destination, items[index].ItemId);
        }

        return true;
    }

    /// <summary>Rowena and the other item-exchange counters, typed via the addon's own value view.</summary>
    private static bool ReadInclusionShop(AtkUnitBase* unit, List<uint> destination)
    {
        var addon = (AddonInclusionShop*)unit;
        var values = addon->TypedAtkValues;
        if (values == null)
            return false;

        var items = values->Items;
        var count = Math.Clamp(ToInt(values->ItemCount), 0, items.Length);

        // The span covers the category the user has selected, so switching the dropdown re-reads
        // and the panel follows along with no extra work.
        for (var i = 0; i < count; i++)
            Append(destination, ToItemId(items[i].ItemId));

        return true;
    }

    /// <summary>
    /// The addons with no struct at all, read by bare index into their AtkValue block.
    /// </summary>
    /// <remarks>
    /// The exact-count check is the whole safety story here. These indices are unnamed numbers
    /// recovered by reverse engineering, and the one thing that reliably changes when the game
    /// reshapes an addon is how many values it carries - so a block that is still exactly the
    /// expected size is good evidence the offsets still point where they used to, and a block that
    /// is not means nothing readable can be salvaged.
    /// </remarks>
    private static bool ReadRawAtkValues(ShopAddonDescriptor descriptor, AtkUnitBase* unit, List<uint> destination)
    {
        var values = unit->AtkValuesSpan;
        if (values.Length != descriptor.ExpectedValueCount)
            return false;

        var count = descriptor.CountIndex >= 0
            ? ToInt(values[descriptor.CountIndex])
            : descriptor.FixedCount;

        var capacity = (values.Length - descriptor.FirstItemIndex) / descriptor.Stride;
        count = Math.Clamp(count, 0, capacity);

        for (var i = 0; i < count; i++)
            Append(destination, ToItemId(values[descriptor.FirstItemIndex + (i * descriptor.Stride)]));

        return true;
    }

    /// <summary>
    /// Normalises a raw client id the way the rest of the plugin does and drops what cannot be a
    /// glamour. HQ stock collapses onto the base id here, which is correct - the dresser stores the
    /// base item - and leaves the caller to deal with the duplicate rows that produces.
    /// </summary>
    private static void Append(List<uint> destination, uint rawId)
    {
        if (rawId == 0)
            return;

        var (itemId, kind) = ItemUtil.GetBaseId(rawId);

        // Event items share the id space behind an offset, so an unfiltered id can collide with a
        // real one.
        if (itemId == 0 || kind == ItemKind.EventItem)
            return;

        destination.Add(itemId);
    }

    private static uint ToItemId(AtkValue value) => value.Type switch
    {
        AtkValueType.UInt => value.UInt,
        AtkValueType.Int => value.Int > 0 ? (uint)value.Int : 0u,
        _ => 0u,
    };

    private static int ToInt(AtkValue value) => value.Type switch
    {
        AtkValueType.Int => value.Int,
        AtkValueType.UInt => value.UInt > int.MaxValue ? 0 : (int)value.UInt,
        _ => 0,
    };
}
