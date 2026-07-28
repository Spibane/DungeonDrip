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
/// "Could not read this" and "nothing here" must stay distinguishable: a wrong marker is worse than
/// no panel, so an unreadable shop has to be able to switch the feature off rather than look empty.
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

    /// <summary>The gil shop, via the one source with a maintained typed struct behind it.</summary>
    private static bool ReadShopEventHandler(List<uint> destination)
    {
        var proxy = ShopEventHandler.AgentProxy.Instance();
        if (proxy == null || proxy->Handler == null)
            return false;

        var handler = proxy->Handler;

        // Buyback lists what you just sold - by definition things you owned moments ago.
        if (handler->BuybackTabActive)
            return true;

        var items = handler->Items;
        var visible = handler->VisibleItems;
        var itemCount = Math.Clamp(handler->ItemsCount, 0, items.Length);
        var visibleCount = Math.Clamp(handler->VisibleItemsCount, 0, visible.Length);

        // Indices into Items, in display order, already filtered by unlock and quest state.
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

        for (var i = 0; i < count; i++)
            Append(destination, ToItemId(items[i].ItemId));

        return true;
    }

    /// <summary>
    /// The addons with no struct at all, read by bare index into their AtkValue block.
    /// </summary>
    /// <remarks>
    /// The exact-count check is the whole safety story. The one thing that reliably changes when the
    /// game reshapes an addon is how many values it carries, so a block of the expected size is good
    /// evidence the offsets still point where they used to - and a wrong size means nothing is
    /// salvageable.
    /// </remarks>
    private static bool ReadRawAtkValues(ShopAddonDescriptor descriptor, AtkUnitBase* unit, List<uint> destination)
    {
        var values = unit->AtkValuesSpan;
        if (values.Length != descriptor.ExpectedValueCount)
            return false;

        // A descriptor whose indices fall outside the block it describes is a typo in the registry,
        // not a shop worth reading. Checked rather than trusted because the alternative is an
        // out-of-range throw inside a draw call.
        if (descriptor.CountIndex >= values.Length ||
            descriptor.FirstItemIndex >= values.Length ||
            descriptor.Stride < 1)
        {
            return false;
        }

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
    /// Normalises a raw client id. HQ stock collapses onto the base id, which is what the dresser
    /// stores; the caller deals with the duplicate rows that produces.
    /// </summary>
    private static void Append(List<uint> destination, uint rawId)
    {
        if (rawId == 0)
            return;

        var (itemId, kind) = ItemUtil.GetBaseId(rawId);

        // Event items share the id space behind an offset and would collide with real gear.
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
