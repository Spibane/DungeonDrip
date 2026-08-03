using System.Collections.Generic;
using DungeonDrip.Core;
using DungeonDrip.Game;

namespace DungeonDrip.Windows;

/// <summary>
/// A panel beside the market board, marking which of the gear it is showing you already own.
/// </summary>
/// <remarks>
/// The browse list, not the listings for one item. By the time the listings window is open you have
/// already chosen the piece, so the question was needed one screen earlier - and a panel there would
/// be one row repeated as many times as people are selling it.
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
    /// You reach a board by travelling to one, and zoning is exactly what wipes the dresser data -
    /// so the snapshot behind this panel is almost always the oldest one the plugin ever draws
    /// from.
    /// </remarks>
    protected override string? UncertainNote =>
        "Your dresser snapshot predates this - check before buying.";
}
