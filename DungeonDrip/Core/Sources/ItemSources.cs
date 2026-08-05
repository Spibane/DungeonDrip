using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Lumina.Excel.Sheets;

namespace DungeonDrip.Core.Sources;

/// <summary>One piece a given currency buys, and what it costs in it.</summary>
/// <param name="Restricted">
/// True when <em>any</em> shop selling this piece is a sell-back counter, which makes the piece
/// event-only - see <see cref="RestrictedVendors"/>.
/// </param>
public readonly record struct CurrencyOffer(uint ItemId, uint Amount, bool Restricted);

/// <summary>
/// Where a piece comes from when it does not come out of a duty: crafted, bought, or given.
/// </summary>
/// <remarks>
/// The sibling of <see cref="DropSources"/>, and the two are complete in opposite ways.
/// <see cref="DropSources"/> reads downloaded loot data that is thin for new content, so it can only
/// ever say "nothing lists this". This reads the client's own sheets, which are exactly right about
/// recipes, shops, quests and achievements - but silent about the Mog Station, seasonal events, PvP
/// series, deep dungeons, treasure maps and relic steps. So neither can say "this cannot be
/// obtained", and every surface has to phrase an empty answer as "nothing in the game's recipe, shop
/// or reward data lists this".
///
/// Built once and never invalidated. These sheets only change when the game does, which means a
/// plugin reload - unlike the loot dataset, which is why that one has a revision counter and this has
/// none.
///
/// Holds two indexes over the one sweep, answering opposite questions under opposite rules -
/// <see cref="For"/> collapsed for display, <see cref="OffersFor"/> uncollapsed for browsing by
/// currency. Neither may adopt the other's rule; see <see cref="Accumulator.FinishByCurrency"/>.
/// </remarks>
public sealed class ItemSources
{
    private static readonly AcquisitionSource[] None = [];
    private static readonly CurrencyOffer[] NoOffers = [];

    private readonly Dictionary<uint, AcquisitionSource[]> byItem;
    private readonly Dictionary<uint, CurrencyOffer[]> byCurrency;

    private ItemSources(
        Dictionary<uint, AcquisitionSource[]> byItem, Dictionary<uint, CurrencyOffer[]> byCurrency)
    {
        this.byItem = byItem;
        this.byCurrency = byCurrency;
    }

    /// <summary>Every known route to this piece, most directly actionable first. Empty when none are.</summary>
    public IReadOnlyList<AcquisitionSource> For(uint itemId) =>
        byItem.TryGetValue(itemId, out var sources) ? sources : None;

    /// <summary>
    /// Every piece this currency buys, cheapest first and once each. Empty when it buys no storable gear.
    /// </summary>
    /// <remarks>
    /// Answers the opposite question to <see cref="For"/> and is not collapsed the way that is - a piece
    /// buyable with two <em>different</em> currencies appears under both. It does appear only once under
    /// each, at the price that currency can actually be expected to take it for: the lowest, except where
    /// the lowest is a seasonal sale. See <see cref="Accumulator.FinishByCurrency"/>,
    /// <see cref="SaleIsSeasonal"/> and <see cref="Build"/>.
    ///
    /// An empty answer is the common case rather than an error: most cost items in the sheets are not
    /// currencies at all but gear being traded up, and no character holds those.
    /// </remarks>
    public IReadOnlyList<CurrencyOffer> OffersFor(uint currencyItemId) =>
        byCurrency.TryGetValue(currencyItemId, out var offers) ? offers : NoOffers;

    /// <summary>
    /// Sweeps every source sheet once and inverts them onto item ids.
    /// </summary>
    /// <remarks>
    /// Framework thread only, like every Lumina read. Measured at about 170ms over the retail sheets,
    /// of which the <c>SpecialShop</c> sweep is some 70ms - well past a frame, which is why
    /// <see cref="Plugin"/> builds this on first demand rather than at load, and comfortably short of
    /// needing the sweeps split up or staged. The elapsed time is logged so that stays a measurement
    /// rather than a belief.
    /// </remarks>
    public static ItemSources Build(StorageEligibility storage)
    {
        var clock = Stopwatch.StartNew();
        var accumulated = new Accumulator(storage);

        CraftSources.Contribute(accumulated);
        ShopSources.Contribute(accumulated);
        QuestSources.Contribute(accumulated);
        AchievementSources.Contribute(accumulated);

        var byItem = accumulated.Finish();

        var byCurrency = new Dictionary<uint, CurrencyOffer[]>();
        var offerCount = 0;
        var chosen = new Dictionary<uint, uint>();
        var unavailable = new HashSet<uint>();

        foreach (var (currencyId, offers) in accumulated.FinishByCurrency())
        {
            // One entry per piece. The same currency commonly prices one piece twice - 158 of the 178
            // pieces MGP buys are listed at two amounts, 323 surplus rows across the sheet - and a list
            // saying a thing costs both 700 and 1,000 states a number nobody would choose to pay.
            var preferHigher = SaleIsSeasonal(currencyId);

            chosen.Clear();
            unavailable.Clear();
            foreach (var offer in offers)
            {
                // Carried across the dedup: two offers for one piece can disagree, and a piece with any
                // route that cannot be used is one the shopping list should leave out.
                if (offer.Restricted)
                    unavailable.Add(offer.ItemId);

                if (!chosen.TryGetValue(offer.ItemId, out var kept))
                {
                    chosen[offer.ItemId] = offer.Amount;
                    continue;
                }

                var better = preferHigher
                    ? System.Math.Max(kept, offer.Amount)
                    : System.Math.Min(kept, offer.Amount);

                chosen[offer.ItemId] = better;
            }

            // Sorted here rather than by each surface that draws a group: the ordering is a property of
            // the index, and doing it per draw would sort hundreds of rows every frame. After the choice
            // above, not before - picking the dearer of two prices and then sorting are different steps.
            var picked = new List<CurrencyOffer>(chosen.Count);
            foreach (var (itemId, amount) in chosen)
                picked.Add(new CurrencyOffer(itemId, amount, unavailable.Contains(itemId)));

            picked.Sort((left, right) => left.Amount.CompareTo(right.Amount));

            byCurrency[currencyId] = [.. picked];
            offerCount += picked.Count;
        }

        // The per-kind figures are routes read, before Finish collapses each piece to one line per
        // kind - so they are the number to check a builder against, not the number of lines drawn.
        Plugin.Log.Information(
            $"Indexed acquisition sources for {byItem.Count} pieces in {clock.ElapsedMilliseconds}ms " +
            $"({offerCount} priced offers across {byCurrency.Count} currencies; " +
            $"routes read: {accumulated.Describe()})");

        return new ItemSources(byItem, byCurrency);
    }

    /// <summary>Manderville Gold Saucer points, whose lower prices are campaign-only.</summary>
    private const uint MgpItemId = 29;

    /// <summary>
    /// Whether this currency's cheaper listing is a limited-time sale rather than a second counter.
    /// </summary>
    /// <remarks>
    /// True only for MGP, and an exception worth its lines because it inverts the rule above. The Gold
    /// Saucer discounts its prices during the Make It Rain campaign, a fortnight a year, and the sheet
    /// carries both the discounted and the standard amount with nothing to mark which is which. Taking
    /// the cheaper one would quote a price unavailable for fifty weeks of the year.
    ///
    /// The shape of the data is what identifies it rather than a claim taken on trust: across all 158
    /// pieces MGP prices twice, the low-to-high ratio is 0.667, 0.7 or 0.714 - one consistent discount of
    /// about 30%. No other currency looks like that. Gil's duplicates run 0.283, 0.717 and 1.0 with no
    /// pattern; Resistance Tokens differ by exactly 3x, which is a tier and not a sale; the raid mythos
    /// and datalog tokens sit at 0.375 and 0.625, which is a weapon against an accessory.
    ///
    /// So this stays a list of one until another currency is shown to behave the same way. Widening it on
    /// the assumption that other event shops must work alike would quietly overstate prices elsewhere.
    /// </remarks>
    private static bool SaleIsSeasonal(uint currencyItemId) => currencyItemId == MgpItemId;

    /// <summary>
    /// The collector the four builders add into, holding the rules they must all obey.
    /// </summary>
    /// <remarks>
    /// A type rather than a bare dictionary so the storage filter and the deduplication cannot be
    /// forgotten by a builder added later - both are enforced on the way in, at
    /// <see cref="Add"/>, instead of being each builder's job to remember.
    /// </remarks>
    internal sealed class Accumulator(StorageEligibility storage)
    {
        private readonly Dictionary<uint, List<AcquisitionSource>> byItem = [];
        private readonly Dictionary<AcquisitionKind, int> counts = [];
        private readonly HashSet<uint> eventOnly = [];

        /// <summary>
        /// Every piece an event sell-back counter stocks, whatever currency and whatever else stocks it.
        /// </summary>
        /// <remarks>
        /// <b>A property of the piece, not of one price for it.</b> The Calamity Salvager having a piece
        /// means most characters were never entitled to it, which is true however it is paid for - so this
        /// is one set rather than a flag per route or per currency. Scoping it per currency let 156 pieces
        /// through: excluded under gil, still listed under a Faire Voucher.
        ///
        /// It also cannot be a route flag, because the other shop listing such a piece is the event's own
        /// vendor - the creepy peddler has Pumpkin Head too, and reading that route as ordinary is exactly
        /// the mistake.
        ///
        /// Recorded outside <see cref="Add"/>'s duplicate test on purpose. That test compares kind, label,
        /// detail, currency and amount, and a piece stocked by both an event counter and an event vendor at
        /// the same price differs in none of them - so whichever route arrived second was dropped, taking
        /// the mark with it.
        /// </remarks>
        public IReadOnlySet<uint> EventOnlyItems => eventOnly;

        /// <summary>Records that a piece is stocked by an event sell-back counter.</summary>
        public void MarkEventOnly(uint itemId) => eventOnly.Add(itemId);

        /// <summary>
        /// Records one route, unless the piece cannot be stored or the route is already known.
        /// </summary>
        /// <remarks>
        /// The storage test comes first because it is what makes this affordable: it throws away the
        /// great majority of shop and quest rewards - materials, consumables, furnishings - before a
        /// name or a cost is ever resolved, and resolving those is the expensive half.
        ///
        /// Then the duplicate test, on the rendered line and the typed price rather than on the shop row
        /// that produced it. The same piece is commonly sold by many rows at one price, and one entry per
        /// row would bury the genuinely different currencies under thirty identical ones. The typed price
        /// is part of the test because two routes can render the same wording and still differ - a piece
        /// every Grand Company sells reads identically for all three and is payable from three balances.
        /// </remarks>
        public void Add(uint itemId, AcquisitionSource source)
        {
            if (!storage.CanBeStored(itemId))
                return;

            if (!byItem.TryGetValue(itemId, out var sources))
                byItem[itemId] = sources = [];

            foreach (var existing in sources)
            {
                if (existing.Kind == source.Kind && existing.Label == source.Label &&
                    existing.Detail == source.Detail &&
                    existing.CurrencyItemId == source.CurrencyItemId &&
                    existing.Amount == source.Amount)
                    return;
            }

            sources.Add(source);
            counts[source.Kind] = counts.GetValueOrDefault(source.Kind) + 1;
        }

        /// <summary>Whether the piece is worth resolving a cost for, for a builder about to do work.</summary>
        /// <remarks>
        /// The same test <see cref="Add"/> applies. Exposed so a builder can skip the sheet reads that
        /// would produce a label and a price for something that is about to be thrown away - the
        /// <c>SpecialShop</c> sweep is only affordable because it asks this first.
        /// </remarks>
        public bool Wanted(uint itemId) => storage.CanBeStored(itemId);

        /// <summary>
        /// Collapses each piece to one line per kind, in the enum's order.
        /// </summary>
        /// <remarks>
        /// <b>One line per kind, not one per route.</b> Listing every route buried the useful ones:
        /// Artisan's Spectacles is handed over for any of eight different materials, so eight lines all
        /// reading "Special Shop - 1 <i>something</i>" pushed the one line anybody wanted - that it also
        /// costs 5,000 seals - past the end of the list. Keeping the first route per kind guarantees no kind
        /// can hide another, and holds every answer to three lines in practice: of the 18,707 pieces
        /// answered, 13,737 have one route, 3,792 have two and 1,178 have three. Six is the structural
        /// ceiling and nothing reaches it. The routes dropped are, by definition, alternative prices at
        /// the same sort of counter.
        ///
        /// <b>A buy-back route is dropped where a real one exists, and renamed where none does.</b> A
        /// special shop charging gil alone is not a way to obtain a piece, so for the 712 Augmented pieces
        /// that also carry their upgrade trade the trade is what shows. The 4 with nothing else, and every
        /// piece an event counter stocks, keep the line and lose the word "Vendor" - see
        /// <see cref="Relabel"/>.
        ///
        /// <b>A gil route is suppressed where an achievement route exists.</b> An achievement reward can
        /// be re-bought from a vendor once claimed, which the sheets record as an ordinary gil sale - so
        /// every such piece looked purchasable outright. This is exact rather than a guess: all 94
        /// achievement-reward pieces in the sheet carry a gil route. The obvious alternative, filtering
        /// on the suspiciously round prices those counters use, was rejected after measuring - 704
        /// pieces with no achievement behind them sit at exactly 1,000 gil and would have lost a real
        /// vendor route.
        /// </remarks>
        public Dictionary<uint, AcquisitionSource[]> Finish()
        {
            var result = new Dictionary<uint, AcquisitionSource[]>(byItem.Count);
            var kept = new List<AcquisitionSource>(KindCount);

            foreach (var (itemId, sources) in byItem)
            {
                var claimable = false;
                var hasRealRoute = false;
                foreach (var source in sources)
                {
                    if (source.Kind == AcquisitionKind.Achievement)
                        claimable = true;

                    if (!source.Repurchase)
                        hasRealRoute = true;
                }

                // Event stock is answered as event stock however it is priced, since none of its routes
                // is open to a character who was not there.
                var eventStock = eventOnly.Contains(itemId);

                kept.Clear();
                foreach (var kind in System.Enum.GetValues<AcquisitionKind>())
                {
                    if (claimable && kind == AcquisitionKind.GilShop)
                        continue;

                    foreach (var source in sources)
                    {
                        if (source.Kind != kind)
                            continue;

                        // A buy-back price is not the answer when the piece can actually be earned. The
                        // real route is usually in another currency - Augmented Ironworks reads "345 gil"
                        // at Rowena's counter and is really the Ironworks piece plus a fee - so keeping
                        // the buy-back would name a number instead of a way to get the thing.
                        if (source.Repurchase && hasRealRoute && !eventStock)
                            continue;

                        kept.Add(Relabel(source, eventStock));
                        break;
                    }
                }

                if (kept.Count > 0)
                    result[itemId] = [.. kept];
            }

            return result;
        }

        /// <summary>
        /// The same routes inverted onto the currency that pays for them, and deliberately not collapsed.
        /// </summary>
        /// <remarks>
        /// <b>Two indexes over one sweep, with opposite rules, and neither may adopt the other's.</b>
        /// <see cref="Finish"/> keeps one route per kind because a piece with eight alternative prices
        /// should not spend eight lines saying so. This keeps all of them, because a piece buyable with
        /// either tomestones or seals belongs under both currencies - collapsing here would hide it from
        /// whichever balance the character actually holds. Merging the two would silently break whichever
        /// guarantee the merge dropped.
        ///
        /// The gil-for-achievement suppression is not applied either. It exists so a re-purchase counter
        /// does not read as a way to obtain a piece; someone browsing what their gil will buy is asking a
        /// question that counter genuinely answers.
        ///
        /// Around 11,700 offers over the retail sheets, so holding both indexes costs nothing worth
        /// measuring.
        /// </remarks>
        public Dictionary<uint, List<CurrencyOffer>> FinishByCurrency()
        {
            var byCurrency = new Dictionary<uint, List<CurrencyOffer>>();

            foreach (var (itemId, sources) in byItem)
            {
                foreach (var source in sources)
                {
                    if (!source.HasPrice)
                        continue;

                    if (!byCurrency.TryGetValue(source.CurrencyItemId, out var offers))
                        byCurrency[source.CurrencyItemId] = offers = [];

                    // Both halves of "cannot actually be bought" are folded in here, since the shopping
                    // list needs one answer: the piece is event stock, or this route is a buy-back price.
                    offers.Add(new CurrencyOffer(
                        itemId, source.Amount, eventOnly.Contains(itemId) || source.Repurchase));
                }
            }

            return byCurrency;
        }

        /// <summary>
        /// Renames a route that is not a way of obtaining the piece, only of getting it back.
        /// </summary>
        /// <remarks>
        /// "Vendor - 345 gil" is a true price and a false answer: it reads as somewhere to go and buy the
        /// thing, when the counter will only hand it back to whoever earned it. The price is kept, because
        /// it is still what the counter charges, and only the route's name changes.
        ///
        /// Event stock is named separately from an ordinary buy-back because the two are different news.
        /// One says the piece is gone unless the character was there for the event; the other says look for
        /// the real route, which is on the line beneath.
        /// </remarks>
        private static AcquisitionSource Relabel(AcquisitionSource source, bool eventStock)
        {
            if (eventStock)
                return source with { Label = "Event re-purchase", EventOnly = true };

            return source.Repurchase ? source with { Label = "Re-purchase" } : source;
        }

        private static readonly int KindCount = System.Enum.GetValues<AcquisitionKind>().Length;

        /// <summary>
        /// Per-kind route totals for the build log. A kind reporting zero is a bug, not a quiet day.
        /// </summary>
        /// <remarks>
        /// Counted as read, before <see cref="Finish"/> collapses a piece to one line per kind, because
        /// this exists to check that each builder found its sheet - and a builder whose routes were all
        /// collapsed away still found them.
        /// </remarks>
        public string Describe()
        {
            var parts = new List<string>();
            foreach (var kind in System.Enum.GetValues<AcquisitionKind>())
                parts.Add($"{kind} {counts.GetValueOrDefault(kind)}");

            return string.Join(", ", parts);
        }
    }

    /// <summary>
    /// An amount with its currency's own name, as the shops phrase their prices.
    /// </summary>
    /// <remarks>
    /// Thousands separators are deliberate and the culture is the invariant one: prices run to five
    /// figures and "18000 gil" is materially harder to read at a glance than "18,000 gil", while a
    /// culture-sensitive separator would make the string disagree with the game's own UI for anyone
    /// whose system is set to a comma-decimal locale.
    /// </remarks>
    internal static string Price(uint amount, string currency) =>
        $"{amount.ToString("N0", CultureInfo.InvariantCulture)} {currency}";

    /// <summary>
    /// The name of a currency item, agreeing in number with the amount it will be printed beside.
    /// </summary>
    /// <remarks>
    /// The game's own <c>Plural</c> column rather than an "s" appended here, because several currencies
    /// do not pluralise on the end - "Allagan Tomestone of Poetics" becomes "Allagan Tomestones of
    /// Poetics" - and the column is also already translated, where a rule written here would only be
    /// right in English.
    ///
    /// Falls back to the singular name when the plural is blank, which some rows are. Returns null when
    /// the row is missing or has no name at all, and callers then drop the price rather than print a
    /// bare number: "375" with no unit is worse than a route with no price attached.
    /// </remarks>
    internal static string? CurrencyName(uint itemId, bool plural)
    {
        if (itemId == 0 || !Plugin.DataManager.GetExcelSheet<Item>().TryGetRow(itemId, out var item))
            return null;

        if (plural)
        {
            var many = item.Plural.ExtractText();
            if (many.Length > 0)
                return many;
        }

        var name = item.Name.ExtractText();
        return name.Length == 0 ? null : name;
    }
}
