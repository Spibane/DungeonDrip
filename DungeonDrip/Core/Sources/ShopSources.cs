using System.Collections.Generic;
using Lumina.Excel.Sheets;

namespace DungeonDrip.Core.Sources;

/// <summary>
/// Everything sold over a counter, and what each till takes: gil, a currency, or seals.
/// </summary>
/// <remarks>
/// Three sheets rather than one, because the game models a gil vendor, a currency exchange and a
/// Grand Company quartermaster as unrelated things. They are collapsed onto one kind of answer here -
/// "bought, for this" - since that is the distinction a player acts on.
///
/// <para><b>Deliberately says nothing about who sells it or where.</b> The chain that would answer
/// that is <c>ENpcBase.ENpcData</c> matched against shop row ids, then the <c>Level</c> sheet for a
/// zone. It was tried and rejected: shops reached through <c>CustomTalk</c>, <c>TopicSelect</c> or
/// <c>PreHandler</c> indirection resolve to no NPC at all, an NPC appears in several <c>Level</c> rows
/// with nothing to say which one is meant, and the two extra sweeps cost more than everything else in
/// this folder combined. The currency and the price are what a player needs to decide, and the linked
/// reference site answers the rest properly.</para>
/// </remarks>
internal static class ShopSources
{
    public static void Contribute(ItemSources.Accumulator into)
    {
        ContributeGilShops(into);
        ContributeSpecialShops(into);
        ContributeGrandCompany(into);
    }

    /// <summary>
    /// Ordinary vendors, priced off the item rather than the shop row.
    /// </summary>
    /// <remarks>
    /// <c>GilShopItem</c> carries no price - the game charges <c>Item.PriceMid</c>, and the shop row
    /// only records that the vendor stocks it. So this sweep exists to establish *that* a piece is
    /// sold at all; the number comes from somewhere else entirely.
    /// </remarks>
    private static void ContributeGilShops(ItemSources.Accumulator into)
    {
        var items = Plugin.DataManager.GetExcelSheet<Item>();

        foreach (var subrows in Plugin.DataManager.GetSubrowExcelSheet<GilShopItem>())
        {
            foreach (var entry in subrows)
            {
                var itemId = entry.Item.RowId;
                if (itemId == 0 || !into.Wanted(itemId))
                    continue;

                var price = items.TryGetRow(itemId, out var item) ? item.PriceMid : 0;
                into.Add(itemId, new AcquisitionSource(
                    AcquisitionKind.GilShop,
                    "Vendor",
                    price > 0 ? ItemSources.Price(price, "gil") : null));
            }
        }
    }

    /// <summary>
    /// Currency exchanges: tomestones, scrips, marks, gemstones and every other special till.
    /// </summary>
    /// <remarks>
    /// The largest sweep here by some way - each row holds up to sixty trades, and each trade nested
    /// lists of what is received and what it costs - but measured at around 70ms of the whole index's
    /// 170ms against the retail sheets, so it needs no special handling beyond the index being lazy.
    ///
    /// Most of what it finds is an item-for-item exchange rather than a currency purchase: the long
    /// tail of costs is one distinct "currency" per upgrade material. Those are real routes and are
    /// kept, which is why naming the currency matters more here than anywhere else - "Special Shop"
    /// alone cannot distinguish a tomestone counter from a material hand-in.
    /// </remarks>
    private static void ContributeSpecialShops(ItemSources.Accumulator into)
    {
        foreach (var shop in Plugin.DataManager.GetExcelSheet<SpecialShop>())
        {
            foreach (var trade in shop.Item)
            {
                var (currencyId, cost) = CostOf(trade);

                // A special shop charging gil is a vendor, whatever sheet describes it - the Calamity
                // Salvager and the junkmonger buyback counters are both modelled this way, and 716
                // storable pieces come through here. Labelling those "Special Shop" would give one
                // transaction two different answers, so they join the gil vendors.
                var kind = currencyId == GilItemId
                    ? AcquisitionKind.GilShop
                    : AcquisitionKind.CurrencyShop;

                var label = kind == AcquisitionKind.GilShop ? "Vendor" : "Special Shop";

                foreach (var received in trade.ReceiveItems)
                {
                    var itemId = received.Item.RowId;
                    if (itemId == 0 || !into.Wanted(itemId))
                        continue;

                    into.Add(itemId, new AcquisitionSource(kind, label, cost));
                }
            }
        }
    }

    /// <summary>Gil, which is an ordinary item row and so turns up as a shop currency like any other.</summary>
    private const uint GilItemId = 1;

    /// <summary>
    /// The first real cost in a trade, as its currency's row id and a rendered price.
    /// </summary>
    /// <remarks>
    /// The id comes back alongside the string because the caller has to recognise gil specifically,
    /// and matching on the rendered name would break the moment the client language changes.
    ///
    /// Only the first cost entry is reported. A trade can demand several things at once, but for gear
    /// that is rare, and "375 Poetics and 1 Grade 4 Glaze" would be wrong more often than the shorter
    /// line is incomplete.
    /// </remarks>
    private static (uint CurrencyId, string? Cost) CostOf(SpecialShop.ItemStruct trade)
    {
        foreach (var cost in trade.ItemCosts)
        {
            var currencyId = cost.ItemCost.RowId;
            if (currencyId == 0 || cost.CurrencyCost == 0)
                continue;

            var currency = ItemSources.CurrencyName(currencyId, cost.CurrencyCost != 1);
            if (currency == null)
                continue;

            return (currencyId, ItemSources.Price(cost.CurrencyCost, currency));
        }

        return (0, null);
    }

    /// <summary>
    /// Quartermaster stock, priced in the named seal of whichever company actually sells it.
    /// </summary>
    /// <remarks>
    /// <b>Nearly all of this stock is company-exclusive, so the seal has to be named.</b> Of the 464
    /// storable pieces here, 445 are sold by exactly one company - Storm Private's Sword by the
    /// Maelstrom, Serpent Private's Sword by the Order of the Twin Adder - and only 19 by all three. A
    /// generic "Grand Company seals" therefore hid a real restriction on 96% of them, which is the
    /// opposite of the reason it was first written that way.
    ///
    /// The 19 sold by every company keep the generic wording, since naming one would then invent a
    /// restriction. Price never varies by company - none of the 621 sheet rows disagree - so one line
    /// serves either way, and no comparison of prices across companies is needed.
    ///
    /// Seal names come off the Item sheet rather than being spelled out here, so they arrive in the
    /// client's own language: company 1, 2 and 3 are Storm, Serpent and Flame Seals at rows 20, 21
    /// and 22. Naming the kind at all matters because "seals" alone is already ambiguous - Allied Seals
    /// and Centurio Seals are separate currencies reached through the special-shop sweep.
    /// </remarks>
    private static void ContributeGrandCompany(ItemSources.Accumulator into)
    {
        // Gathered before anything is emitted, because the answer for a piece depends on how many
        // companies stock it - and that is not known until every subrow has been seen. Emitting per
        // subrow instead would produce three routes for the shared items, of which the collapse to one
        // line per kind would keep whichever came first and so name a single company at random.
        var stock = new Dictionary<uint, (uint Seals, HashSet<uint> Companies)>();
        var categories = Plugin.DataManager.GetExcelSheet<GCScripShopCategory>();

        foreach (var subrows in Plugin.DataManager.GetSubrowExcelSheet<GCScripShopItem>())
        {
            if (!categories.TryGetRow(subrows.RowId, out var category))
                continue;

            var company = category.GrandCompany.RowId;
            if (company is 0 or > 3)
                continue;

            foreach (var entry in subrows)
            {
                var itemId = entry.Item.RowId;
                if (itemId == 0 || !into.Wanted(itemId))
                    continue;

                if (!stock.TryGetValue(itemId, out var known))
                    stock[itemId] = known = (entry.CostGCSeals, []);

                known.Companies.Add(company);

                // Prices agree across companies throughout the sheet, so the first seen stands. Kept as
                // "first wins" rather than asserting equality: a patch that broke the rule should show
                // one company's price, not throw inside a sheet sweep.
                if (known.Seals == 0)
                    stock[itemId] = (entry.CostGCSeals, known.Companies);
            }
        }

        foreach (var (itemId, (seals, companies)) in stock)
        {
            if (seals == 0)
            {
                into.Add(itemId, new AcquisitionSource(AcquisitionKind.GrandCompany, "Grand Company"));
                continue;
            }

            // One company means the seal can be named, which is also a statement about where the piece
            // can be got at all. All three means it cannot be narrowed, and must not appear to be.
            var currency = companies.Count == 1
                ? ItemSources.CurrencyName(FirstSealItemId + FirstOf(companies) - 1, seals != 1)
                : null;

            into.Add(itemId, new AcquisitionSource(
                AcquisitionKind.GrandCompany,
                "Grand Company",
                ItemSources.Price(seals, currency ?? "Grand Company seals")));
        }
    }

    /// <summary>Storm Seal, with Serpent and Flame following it - so company id plus this, less one.</summary>
    private const uint FirstSealItemId = 20;

    private static uint FirstOf(HashSet<uint> companies)
    {
        foreach (var company in companies)
            return company;

        return 0;
    }
}
