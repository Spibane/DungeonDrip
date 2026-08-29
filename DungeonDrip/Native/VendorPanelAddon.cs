using System.Collections.Generic;
using DungeonDrip.Core;
using DungeonDrip.Presentation;

namespace DungeonDrip.Native;

/// <summary>The vendor panel as a game window: gear the shop stocks, and where each piece is held.</summary>
/// <remarks>
/// The first surface drawn natively, and picked because it carries every hard part of a gear list in
/// the fewest lines - a marker whose shape and colour are separate decisions, an item icon, a name,
/// a multi-line tooltip - while riding on an addon that appears and disappears as the player walks
/// up to a counter.
/// </remarks>
internal sealed class VendorPanelAddon(Plugin plugin, GlyphTextures glyphs)
    : NativePanelAddon(plugin, glyphs, "DungeonDripVendor", "Vendor Drip")
{
    protected override PanelSettings Settings => Plugin.Configuration.VendorPanel;

    protected override string? AnchorAddonName => Plugin.Shop.ActiveAddonName;

    protected override string CollapsePrefix => "vendor:";

    protected override IReadOnlyList<GearRow>? ResolveRows() => Plugin.Shop.Resolve();

    protected override string? UncertainNote => PanelText.VendorUncertain;
}
