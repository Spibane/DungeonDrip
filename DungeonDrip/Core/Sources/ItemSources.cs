using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Lumina.Excel.Sheets;

namespace DungeonDrip.Core.Sources;

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
/// </remarks>
public sealed class ItemSources
{
    private static readonly AcquisitionSource[] None = [];

    private readonly Dictionary<uint, AcquisitionSource[]> byItem;

    private ItemSources(Dictionary<uint, AcquisitionSource[]> byItem) => this.byItem = byItem;

    /// <summary>Every known route to this piece, most directly actionable first. Empty when none are.</summary>
    public IReadOnlyList<AcquisitionSource> For(uint itemId) =>
        byItem.TryGetValue(itemId, out var sources) ? sources : None;

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

        // The per-kind figures are routes read, before Finish collapses each piece to one line per
        // kind - so they are the number to check a builder against, not the number of lines drawn.
        Plugin.Log.Information(
            $"Indexed acquisition sources for {byItem.Count} pieces in {clock.ElapsedMilliseconds}ms " +
            $"(routes read: {accumulated.Describe()})");

        return new ItemSources(byItem);
    }

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

        /// <summary>
        /// Records one route, unless the piece cannot be stored or the route is already known.
        /// </summary>
        /// <remarks>
        /// The storage test comes first because it is what makes this affordable: it throws away the
        /// great majority of shop and quest rewards - materials, consumables, furnishings - before a
        /// name or a cost is ever resolved, and resolving those is the expensive half.
        ///
        /// Then the duplicate test, on the rendered line rather than on the shop row that produced it.
        /// The same piece is commonly sold by many rows at one price, and one line per row would bury
        /// the two genuinely different currencies under thirty identical entries.
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
                    existing.Detail == source.Detail)
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
                var repurchasable = false;
                foreach (var source in sources)
                {
                    if (source.Kind == AcquisitionKind.Achievement)
                        repurchasable = true;
                }

                kept.Clear();
                foreach (var kind in System.Enum.GetValues<AcquisitionKind>())
                {
                    if (repurchasable && kind == AcquisitionKind.GilShop)
                        continue;

                    foreach (var source in sources)
                    {
                        if (source.Kind != kind)
                            continue;

                        kept.Add(source);
                        break;
                    }
                }

                if (kept.Count > 0)
                    result[itemId] = [.. kept];
            }

            return result;
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
