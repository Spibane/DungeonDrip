using System.Collections.Generic;
using DungeonDrip.Core;
using DungeonDrip.Presentation;

namespace DungeonDrip.Windows;

/// <summary>
/// A panel that rides alongside a vendor window, listing the glamour gear it stocks and where each
/// piece is already held.
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

    protected override string? UncertainNote => PanelText.VendorUncertain;
}
