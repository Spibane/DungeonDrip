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
/// <para><b>The id array does not shrink.</b> Switching from a category with a hundred results to
/// one with twenty leaves the previous eighty ids sitting past the end, and reading the whole array
/// would put body armour on the panel at a dye vendor. The live count comes from the catalogue
/// proxy instead, and everything past it is stale by definition.</para>
/// </remarks>
public static unsafe class MarketBoardReader
{
    /// <summary>Rows are read into a caller-owned list so the per-frame path allocates nothing.</summary>
    /// <returns>False when the browse list could not be read and the panel must not draw.</returns>
    public static bool TryRead(List<uint> destination)
    {
        destination.Clear();

        var agent = AgentItemSearch.Instance();
        if (agent == null)
            return false;

        var proxy = InfoProxyCatalogSearch.Instance();
        if (proxy == null)
            return false;

        var ids = agent->ListingPageItemIds;

        // Everything past the live count belongs to whatever category was open before.
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
