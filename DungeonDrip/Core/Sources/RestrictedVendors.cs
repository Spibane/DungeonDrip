using System.Collections.Generic;
using Lumina.Excel.Sheets;

namespace DungeonDrip.Core.Sources;

/// <summary>
/// The NPCs whose counters only sell back what a character already earned.
/// </summary>
/// <remarks>
/// One of two sources of that restriction, and the one that needs a list. The other needs none: a
/// <c>SpecialShop</c> trade whose only cost is gil is a buy-back price wherever it appears, which
/// <see cref="ShopSources"/> recognises from the trade itself.
///
/// The Calamity Salvager and the Recompense Officer stock seasonal and event gear - pumpkin heads,
/// Starlight capes, yukata, wedding attire - but only for a character who took part at the time. The
/// sheets record the stock and not the entitlement, so a list built from them offers 337 pieces that most
/// characters cannot buy at any price. The gil buy-back rule in <see cref="ShopSources"/> accounts for a
/// further 716.
///
/// <b>Appearing in their stock is what makes a piece event-only, and one route is enough to say so.</b>
/// A third of those are also listed against another shop, which reads like evidence the piece is
/// ordinarily for sale and is the opposite: those shops belong to the event vendors themselves - the
/// creepy peddler, the Starlight supplier, the House Valentione maid, the Moonfire Faire vendor, the
/// Little Lady, the uma and hitsuji shonin - who are gone from the world the rest of the year. Checked
/// against all 102 of them, and every one is a seasonal piece with no permanent vendor anywhere.
///
/// So a piece these counters stock is excluded whatever else lists it. Requiring every route to be a
/// sell-back route was tried first and let all 102 through.
///
/// <b>Identified by NPC row id rather than by name.</b> The names in the sheet are localised, and
/// "Calamity Salvager" would match nothing on a French or Japanese client. Row ids do not move for an
/// NPC that already exists.
///
/// <b>The shops are resolved from the NPCs rather than listed directly.</b> Hardcoding the fifteen shop
/// rows would be shorter and would go quietly stale: a shop added to the Salvager in a patch would start
/// appearing again with nothing to notice it. Reading the handler list costs six rows, not a sweep.
///
/// This is the one place the NPC chain is used, and it works here for the reason it failed in
/// <see cref="ShopSources"/>: going from a known NPC to its shops is a list lookup, where going from
/// every shop back to an NPC has to survive <c>CustomTalk</c> and <c>PreHandler</c> indirection that
/// often leads nowhere.
/// </remarks>
internal static class RestrictedVendors
{
    /// <summary>
    /// Calamity Salvager and Recompense Officer, three rows each - one per starting city.
    /// </summary>
    /// <remarks>
    /// All three of each carry identical handler lists, so which one is read does not matter. They are
    /// all listed anyway, because a patch that gave one city its own stock would otherwise be missed
    /// silently.
    /// </remarks>
    private static readonly uint[] NpcRows =
    [
        1006004, 1006005, 1006006,
        1017613, 1017614, 1017615,
    ];

    /// <summary>
    /// Every shop row those NPCs offer, whichever sheet the row lives in.
    /// </summary>
    /// <remarks>
    /// Handler ids are not typed, so this is the union of everything the NPCs point at - 29 ids, of which
    /// 6 turn out to be <c>SpecialShop</c> rows and 9 <c>GilShopItem</c> rows, and the rest are quests,
    /// custom talk and the like. Callers test membership rather than assuming a handler is a shop, so the
    /// untyped extras cost nothing but a slightly larger set.
    /// </remarks>
    public static HashSet<uint> ShopRows()
    {
        var rows = new HashSet<uint>();
        var sheet = Plugin.DataManager.GetExcelSheet<ENpcBase>();

        foreach (var npc in NpcRows)
        {
            if (!sheet.TryGetRow(npc, out var row))
                continue;

            foreach (var handler in row.ENpcData)
            {
                if (handler.RowId != 0)
                    rows.Add(handler.RowId);
            }
        }

        return rows;
    }
}
