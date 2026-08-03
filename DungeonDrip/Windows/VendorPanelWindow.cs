using System.Collections.Generic;
using DungeonDrip.Core;

namespace DungeonDrip.Windows;

/// <summary>
/// A panel that rides alongside a vendor window, listing the glamour gear it stocks and where you
/// already have each piece.
/// </summary>
/// <remarks>
/// It lists what the vendor is currently showing, not what is on screen: a category change re-reads,
/// scrolling does not.
/// </remarks>
public sealed class VendorPanelWindow(Plugin plugin)
    : AddonPanelWindow(plugin, "Vendor Drip###DungeonDripVendorPanel", "vendor:", "vendor")
{
    protected override PanelSettings Settings => Plugin.Configuration.VendorPanel;

    protected override string? AnchorAddonName => Plugin.Shop.ActiveAddonName;

    protected override IReadOnlyList<GearRow>? ResolveRows() => Plugin.Shop.Resolve();

    /// <summary>
    /// You reach a vendor by walking to one, so the dresser snapshot is usually old by then, and the
    /// mistake this prevents costs actual gil.
    /// </summary>
    protected override string? UncertainNote =>
        "Your dresser snapshot predates this - check before buying.";
}
