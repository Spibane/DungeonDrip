using System;
using System.Collections.Generic;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace DungeonDrip.Game;

/// <summary>
/// Pulls the item ids the market board is currently browsing out of the client.
/// </summary>
/// <remarks>
/// Not read off the addon's AtkValues, unlike the shops. The board keeps its browse results in the
/// search agent, and the agent is both easier to read and stable while you scroll - the values
/// block holds category icons and nothing about what is listed.
///
/// <para>The agent holds the ids and the addon's own results list says how many of them are being
/// shown. That split matters because the agent's array is a cache the game writes over without
/// shrinking - it always holds a hundred ids, most of them left behind by whatever was open
/// before.</para>
///
/// <para>The count has to come from the list rather than from the catalogue proxy, which was the
/// first thing tried and is only right for category browsing. Measured: picking a category walks
/// the proxy's count up 20 at a time as pages arrive, but typing a search leaves it frozen at
/// whatever the category left while the search's results are written over the front of the array.
/// Reading a hundred ids then gave three results followed by ninety-seven stale ones. The proxy's
/// own entry list is worse still - it stops at the first page and never catches up.</para>
/// </remarks>
public static unsafe class MarketBoardReader
{
    /// <summary>Rows are read into a caller-owned list so the per-frame path allocates nothing.</summary>
    /// <returns>False when the browse list could not be read and the panel must not draw.</returns>
    public static bool TryRead(AtkUnitBase* unit, List<uint> destination)
    {
        destination.Clear();

        var agent = AgentItemSearch.Instance();
        if (agent == null || unit == null)
            return false;

        var list = ((AddonItemSearch*)unit)->ResultsList;
        if (list == null)
            return false;

        var ids = agent->ListingPageItemIds;

        // Everything past what the list is showing belongs to a previous category or search.
        var count = Math.Clamp(list->ListLength, 0, ids.Length);

        for (var i = 0; i < count; i++)
        {
            var rawId = ids[i];
            if (rawId == 0)
                continue;

            var (itemId, kind) = ItemUtil.GetBaseId(rawId);

            // Event items share the id space behind an offset and would collide with real gear.
            if (itemId == 0 || kind == ItemKind.EventItem)
                continue;

            destination.Add(itemId);
        }

        return true;
    }
}
