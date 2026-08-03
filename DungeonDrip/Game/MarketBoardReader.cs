using System;
using System.Collections.Generic;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;

namespace DungeonDrip.Game;

/// <summary>
/// Pulls the item ids the market board is currently browsing out of the client.
/// </summary>
/// <remarks>
/// Not read off the addon's AtkValues, unlike the shops. The board keeps its browse results in the
/// search agent, and the agent is both easier to read and stable while you scroll - the values
/// block holds category icons and nothing about what is listed.
///
/// <para>The agent holds the ids and the catalogue proxy holds the count. The agent's array is a
/// cache the game writes over without shrinking, so it keeps whatever the last category left
/// behind; the count is what says how much of it is current.</para>
///
/// <para><b>Known wrong for text searches.</b> Searching inside a category leaves both sets of
/// results in the array and the count covers both, so the panel lists the category's gear as well
/// as the search's. Reading the proxy's own entries instead was tried and is worse - it lags a
/// category behind and drops results - so this is the better of the two until the search case has
/// actually been measured rather than guessed at.</para>
/// </remarks>
public static unsafe class MarketBoardReader
{
    /// <summary>Rows are read into a caller-owned list so the per-frame path allocates nothing.</summary>
    /// <returns>False when the browse list could not be read and the panel must not draw.</returns>
    public static bool TryRead(List<uint> destination)
    {
        destination.Clear();

        var proxy = InfoProxyCatalogSearch.Instance();
        if (proxy == null)
            return false;

        var agent = AgentItemSearch.Instance();
        if (agent == null)
            return false;

        var ids = agent->ListingPageItemIds;
        var count = Math.Clamp((int)proxy->EntryCount, 0, ids.Length);

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
