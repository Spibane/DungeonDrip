using System.Collections.Generic;
using DungeonDrip.Core.Sources;
using DungeonDrip.Game;
using Lumina.Excel.Sheets;

namespace DungeonDrip.Core;

/// <summary>One unowned piece a held currency buys, and whether the balance covers it.</summary>
public sealed record ShoppingPiece(
    uint ItemId, string Name, ushort IconId, uint Cost, bool Affordable);

/// <summary>
/// One currency, what is in it, and the unowned gear it buys.
/// </summary>
/// <param name="Affordable">
/// How many of <paramref name="Pieces"/> the balance covers, counted here rather than by whoever draws
/// it - the heading needs the number before the rows are walked.
/// </param>
public sealed record CurrencyGroup(
    uint CurrencyItemId,
    string Name,
    long Balance,
    IReadOnlyList<ShoppingPiece> Pieces,
    int Affordable);

/// <summary>Every currency worth showing, in the order worth showing them.</summary>
public sealed record ShoppingListReport(IReadOnlyList<CurrencyGroup> Groups)
{
    public bool IsEmpty => Groups.Count == 0;
}

/// <summary>
/// Answers "what can this balance buy that is not already collected", per currency held.
/// </summary>
/// <remarks>
/// The inverse of the question <see cref="Sources.ItemSources"/> answers per piece, and the reason that
/// index keeps an uncollapsed by-currency view. A piece buyable with either tomestones or seals belongs
/// under both, so this reads <see cref="Sources.ItemSources.OffersFor"/> rather than walking pieces and
/// asking what each costs.
///
/// A static <c>Build</c> returning an immutable report and holding no cache of its own, the same shape
/// as <see cref="DresserPressure"/> and <see cref="CarriedGear"/> - the caller owns the caching,
/// because the caller is what knows when its inputs moved.
/// </remarks>
public static class ShoppingList
{
    private static readonly ShoppingListReport Empty = new([]);

    /// <summary>
    /// Builds a group per held currency that buys something not already collected.
    /// </summary>
    /// <remarks>
    /// Ordering is load-bearing in two places and neither is arbitrary. Within a group, affordable
    /// pieces lead and the rest follow, each half already cheapest-first from the index - so the top of
    /// every group is the part that can be acted on now, and what is being saved towards is directly
    /// beneath it rather than interleaved. Between groups, the most affordable count leads, so the
    /// currency with something to spend on is the one at the top of the section.
    ///
    /// A currency whose every piece is already collected produces no group at all rather than an empty
    /// one. An empty group would be a line spent saying there is nothing to say, and there are a dozen
    /// of them on a character who has played for a while.
    ///
    /// <see cref="Configuration.ExcludeSellBackVendors"/> is applied here rather than when the index is
    /// built, and deliberately: the index is built once per load and never invalidated, so a setting
    /// baked into it could not be turned off without a reload.
    ///
    /// The same disappearance is how <see cref="Configuration.ReadyToBuyOutfitsOnly"/> reads: a currency
    /// buying no outfit pieces at all drops out rather than lingering as an empty heading. Several do -
    /// Sack of Nuts prices 450 pieces and not one of them is in a set - so with the filter on the section
    /// is shorter by whole groups as well as by rows.
    /// </remarks>
    public static ShoppingListReport Build(
        IReadOnlyDictionary<uint, long> balances,
        Sources.ItemSources sources,
        OwnershipView ownership,
        OutfitCatalog outfits,
        StorageEligibility storage,
        Configuration configuration,
        JobFilter jobFilter,
        EquipLockFilter equipLocks)
    {
        if (balances.Count == 0)
            return Empty;

        var items = Plugin.DataManager.GetExcelSheet<Item>();
        var groups = new List<CurrencyGroup>();

        foreach (var (currencyId, balance) in balances)
        {
            var offers = sources.OffersFor(currencyId);
            if (offers.Count == 0)
                continue;

            var name = Sources.ItemSources.CurrencyName(currencyId, plural: true);
            if (name == null)
                continue;

            var affordable = new List<ShoppingPiece>();
            var rest = new List<ShoppingPiece>();

            foreach (var offer in offers)
            {
                if (!items.TryGetRow(offer.ItemId, out var item))
                    continue;

                // The same three tests every other gear list applies, in the same order, so this list
                // cannot come to disagree with the duty list about what is worth showing.
                if (!storage.MatchesScope(storage.Of(item), configuration.Scope))
                    continue;

                if (configuration.OnlyCurrentJobEquippable && !jobFilter.CanEquip(item))
                    continue;

                if (equipLocks.Hides(item, configuration))
                    continue;

                var (slotOrder, _) = EquipSlots.Describe(item.EquipSlotCategory.Value);
                if (configuration.HideWeapons && EquipSlots.IsWeaponSlot(slotOrder))
                    continue;

                // Asked before ownership is resolved, which is the cheaper of the two tests and throws
                // away six pieces in seven when it is on.
                if (configuration.ReadyToBuyOutfitsOnly && outfits.SetsContaining(offer.ItemId).Count == 0)
                    continue;

                // Being in a sell-back counter's stock is enough: that makes the piece event gear, and any
                // other shop listed against it is the event's own vendor rather than a year-round one.
                if (configuration.ExcludeSellBackVendors && offer.Restricted)
                    continue;

                var owned = MissingItems.Resolve(
                    offer.ItemId,
                    ownership,
                    outfits.SetsContaining(offer.ItemId),
                    configuration.OutfitOwnership,
                    configuration.Scope);

                if (owned != OwnershipSource.None)
                    continue;

                var within = offer.Amount <= balance;
                var piece = new ShoppingPiece(
                    offer.ItemId, item.Name.ExtractText(), item.Icon, offer.Amount, within);

                (within ? affordable : rest).Add(piece);
            }

            if (affordable.Count == 0 && rest.Count == 0)
                continue;

            // Concatenated rather than sorted: both halves arrive cheapest-first from the index, and
            // re-sorting the join would only undo that.
            var pieces = new List<ShoppingPiece>(affordable.Count + rest.Count);
            pieces.AddRange(affordable);
            pieces.AddRange(rest);

            groups.Add(new CurrencyGroup(currencyId, name, balance, pieces, affordable.Count));
        }

        groups.Sort((left, right) =>
        {
            var byAffordable = right.Affordable.CompareTo(left.Affordable);
            return byAffordable != 0
                ? byAffordable
                : string.Compare(left.Name, right.Name, System.StringComparison.OrdinalIgnoreCase);
        });

        return new ShoppingListReport(groups);
    }
}
