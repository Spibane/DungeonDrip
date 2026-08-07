using System.Collections.Generic;
using DungeonDrip.Core;
using DungeonDrip.Game;
using DungeonDrip.Presentation;

namespace DungeonDrip.Native;

/// <summary>The market board panel as a game window, marking what is already collected.</summary>
/// <remarks>
/// The browse list, not the listings for one item. By the time the listings window is open the piece
/// has already been chosen, so the question was needed one screen earlier - and a panel there would
/// be one row repeated as many times as people are selling it.
/// </remarks>
internal sealed class MarketPanelAddon(Plugin plugin, GlyphTextures glyphs)
    : NativePanelAddon(plugin, glyphs, "DungeonDripMarket", "Market Drip")
{
    protected override PanelSettings Settings => Plugin.Configuration.MarketBoardPanel;

    protected override string AnchorAddonName => MarketBoardWatcher.AddonName;

    protected override string CollapsePrefix => "market:";

    protected override IReadOnlyList<GearRow>? ResolveRows() => Plugin.Market.Resolve();

    protected override string? UncertainNote => PanelText.MarketUncertain;
}
