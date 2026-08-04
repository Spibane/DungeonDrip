using System.Collections.Generic;
using DungeonDrip.Core;
using DungeonDrip.Game;

namespace DungeonDrip.Windows;

/// <summary>
/// A panel beside the market board, marking which of the gear it is showing is already collected.
/// </summary>
/// <remarks>
/// The browse list, not the listings for one item. By the time the listings window is open the piece
/// has already been chosen, so the question was needed one screen earlier - and a panel there would be
/// one row repeated as many times as people are selling it.
/// </remarks>
public sealed class MarketBoardPanelWindow(Plugin plugin)
    : AddonPanelWindow(plugin, "Market Drip###DungeonDripMarketPanel", "market:", "market")
{
    protected override PanelSettings Settings => Plugin.Configuration.MarketBoardPanel;

    protected override string AnchorAddonName => MarketBoardWatcher.AddonName;

    protected override IReadOnlyList<GearRow>? ResolveRows() => Plugin.Market.Resolve();

    /// <summary>
    /// Staleness bites hardest here of anywhere, and the mistake it prevents costs gil.
    /// </summary>
    /// <remarks>
    /// A board is reached by travelling to one, and zoning is exactly what wipes the dresser data -
    /// so the snapshot behind this panel is almost always the oldest one the plugin ever draws
    /// from.
    /// </remarks>
    protected override string? UncertainNote =>
        "Your dresser snapshot predates this - check before buying.";
}
