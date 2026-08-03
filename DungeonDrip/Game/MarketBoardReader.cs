using System;
using System.Collections.Generic;
using Dalamud.Utility;
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
/// <para>Read from the catalogue proxy's own entries rather than the agent's page of ids. The
/// agent's array is a cache the game writes over and does not shrink, so it keeps whatever the last
/// category left behind - and searching within a category leaves both sets of results in it at
/// once, which put the category's gear on the panel beside the search's. The proxy holds the result
/// set the board is actually showing.</para>
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

        var entries = proxy->Entries;
        var count = Math.Clamp((int)proxy->EntryCount, 0, entries.Length);

        for (var i = 0; i < count; i++)
        {
            var rawId = entries[i].ItemId;
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
