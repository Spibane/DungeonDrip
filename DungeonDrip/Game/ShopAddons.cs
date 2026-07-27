using System.Collections.Generic;

namespace DungeonDrip.Game;

/// <summary>Where a vendor's stock list is read from.</summary>
public enum ShopSource
{
    /// <summary>The gil shop's event handler, which exposes a typed, already-sorted item array.</summary>
    ShopEventHandler,

    /// <summary>AddonInclusionShop's own typed view over its AtkValues.</summary>
    InclusionShop,

    /// <summary>Bare AtkValue indices, because no typed struct exists for the addon.</summary>
    RawAtkValues,
}

/// <param name="ExpectedValueCount">
/// The addon's AtkValue count when the layout is the one described here. Checked exactly before any
/// raw read: if a patch resizes the block, the offsets below are meaningless and the only safe move
/// is to stop. Unused by the typed sources.
/// </param>
/// <param name="CountIndex">Index of the AtkValue holding the row count, or -1 when it is fixed.</param>
/// <param name="FixedCount">Row count when the addon always allocates the same number of slots.</param>
/// <param name="FirstItemIndex">Index of the first row's item id.</param>
/// <param name="Stride">Distance between consecutive rows' item ids.</param>
public sealed record ShopAddonDescriptor(
    string AddonName,
    ShopSource Source,
    int ExpectedValueCount = 0,
    int CountIndex = -1,
    int FixedCount = 0,
    int FirstItemIndex = 0,
    int Stride = 1);

/// <summary>
/// Which addons count as a vendor, and how to get the stock list out of each.
/// </summary>
/// <remarks>
/// This table is the fragile part of the vendor feature, so it is kept as data in one place: adding
/// a vendor, or repairing one after a patch, is a line here rather than a code change.
///
/// Two of the sources are typed and maintained upstream in FFXIVClientStructs, so they break loudly
/// (a null pointer) rather than silently. The rest have no struct at all and are read by raw
/// AtkValue index - numbers with no names on them, taken from what other plugins have reverse
/// engineered, and guarded by <see cref="ShopAddonDescriptor.ExpectedValueCount"/> so a drifted
/// layout disables the addon instead of feeding garbage item ids into the panel.
///
/// Deliberately absent: the Gold Saucer prize exchange and MerchantShop (layout unconfirmed - they
/// render nothing until someone reports the addon name), CollectablesShop and MJIDisposeShop (turn-in
/// and sell-only), FittingShop (ornaments, not equipment), TripleTriadCoinExchange (cards), and the
/// market board, where N listings of one item would produce N identical rows.
/// </remarks>
public static class ShopAddons
{
    private static readonly ShopAddonDescriptor[] All =
    [
        // Gil vendors and the Calamity Salvager. Typed, and its VisibleItems array is already the
        // game's own filtered and sorted display order.
        new("Shop", ShopSource.ShopEventHandler),

        // Rowena's House of Splendors and the other "item exchange" NPCs. Typed via
        // AddonInclusionShop.TypedAtkValues, whose Items span covers the selected category only -
        // which is exactly what the panel wants to list.
        new("InclusionShop", ShopSource.InclusionShop),

        // Tomestone, scrip, Bicolor Gemstone and Nut vendors. Both addons share a layout.
        new("ShopExchangeCurrency", ShopSource.RawAtkValues,
            ExpectedValueCount: 3325, CountIndex: 4, FirstItemIndex: 1066),
        new("ShopExchangeItem", ShopSource.RawAtkValues,
            ExpectedValueCount: 3325, CountIndex: 4, FirstItemIndex: 1066),

        // Grand Company quartermasters always allocate the same number of slots.
        new("GrandCompanyExchange", ShopSource.RawAtkValues,
            ExpectedValueCount: 556, FixedCount: 50, FirstItemIndex: 317),

        new("FreeShop", ShopSource.RawAtkValues,
            ExpectedValueCount: 565, CountIndex: 76, FirstItemIndex: 138),

        // The Firmament. Note the trailing 2 - SkyIslandExchange is the agent, not the addon.
        new("SkyIslandExchange2", ShopSource.RawAtkValues,
            ExpectedValueCount: 461, CountIndex: 0, FirstItemIndex: 56),
    ];

    private static readonly Dictionary<string, ShopAddonDescriptor> ByName =
        BuildIndex();

    /// <summary>Every addon name to listen on, for a single bulk lifecycle registration.</summary>
    public static IReadOnlyList<string> Names { get; } = [.. ByName.Keys];

    public static bool TryGet(string addonName, out ShopAddonDescriptor descriptor) =>
        ByName.TryGetValue(addonName, out descriptor!);

    private static Dictionary<string, ShopAddonDescriptor> BuildIndex()
    {
        var index = new Dictionary<string, ShopAddonDescriptor>(All.Length);
        foreach (var descriptor in All)
            index[descriptor.AddonName] = descriptor;

        return index;
    }
}
