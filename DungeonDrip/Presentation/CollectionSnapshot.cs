using System.Collections.Generic;
using System.Linq;
using DungeonDrip.Core;
using DungeonDrip.Game;

namespace DungeonDrip.Presentation;

/// <summary>
/// The collection view's four reports, rebuilt only when something behind them has moved.
/// </summary>
/// <remarks>
/// Held apart from the window because none of it is a drawing decision - the reports, when they are
/// worth rebuilding, and how the held pieces sort and group are all answers about the collection,
/// and the second backend has to reach the same ones. The window is left holding what is genuinely
/// its own: which tab is open, which headings are folded, how many rows to show before the "show
/// all" toggle.
///
/// Not a record built fresh each frame. Rebuilding the reports costs a sheet sweep and several
/// passes over the containers, so what this really is is the memoisation, and the four reports are
/// what it is memoising.
/// </remarks>
internal sealed class CollectionSnapshot(Plugin plugin)
{
    private int seenOwnershipRevision = -1;
    private Inputs seenInputs;
    private ulong seenInventory;
    private ulong seenCurrency;

    /// <summary>How full the dresser is. Null until a snapshot has been read.</summary>
    public DresserPressureReport? Pressure { get; private set; }

    /// <summary>Carried gear measured against the collection. Null before the first rebuild.</summary>
    public CarriedGearReport? Carried { get; private set; }

    /// <summary>What the currencies held will buy. Null while the setting is off.</summary>
    public ShoppingListReport? Shopping { get; private set; }

    /// <summary>Outfit sets part way through, closest to done first. Empty without dresser data.</summary>
    public IReadOnlyList<SetStanding> Sets { get; private set; } = [];

    /// <summary>
    /// Rebuilds what has moved, and nothing else. Call once a frame before reading anything above.
    /// </summary>
    /// <remarks>
    /// Three keys, because the tabs do not read the same things and only one of those things
    /// announces when it moves.
    ///
    /// The carried list needs its own. The ownership tracker only bumps its revision for inventory
    /// changes while "also count bags" is on, since that setting is the only reason it reads the
    /// bags at all - but that list reads them regardless, on purpose, because it asks the
    /// opposite question. With the setting off, which is the default, throwing a piece away left it
    /// on the list with nothing that could ever clear it.
    ///
    /// The shopping list needs a third for the same reason again: nothing bumps the ownership revision
    /// when a balance moves, so spending a currency would leave the list quoting the old total.
    ///
    /// Staleness of the snapshot is deliberately in none of them, matching the tracker's own rule:
    /// an ageing snapshot changes how the answer should be worded, not what it is.
    /// </remarks>
    public void Refresh()
    {
        var inputs = Capture();
        var ownershipMoved = plugin.Ownership.Revision != seenOwnershipRevision || inputs != seenInputs;

        // One pass over the containers, no allocation, and the only thing that notices a piece
        // leaving the bags when the tracker has no reason to look.
        var inventory = InventoryReader.Fingerprint();
        var inventoryMoved = inventory != seenInventory;

        // A third key, for the same reason as the second: nothing bumps the ownership revision when a
        // balance moves, so spending a currency would otherwise leave the list claiming the old total.
        var currency = CurrencyReader.Fingerprint();
        var currencyMoved = currency != seenCurrency;

        if (!ownershipMoved && !inventoryMoved && !currencyMoved)
            return;

        seenOwnershipRevision = plugin.Ownership.Revision;
        seenInputs = inputs;
        seenInventory = inventory;
        seenCurrency = currency;

        var ownership = plugin.Ownership.Current;

        // Held gear is the collection compared against everywhere something is merely held, so
        // either side moving rebuilds it. The retainers go in regardless of whether they are being
        // counted as owning a piece, for the same reason the bags do: that setting decides what makes
        // a piece collected, and this section is about the holdings themselves either way.
        Carried = CarriedGear.Build(
            InventoryReader.ReadDetailed(), plugin.Ownership.Retainers,
            ownership, plugin.Outfits, plugin.Storage, plugin.Configuration.OutfitOwnership);

        // Rebuilt when the balances move as well as when the collection does, since either changes what
        // is worth buying. Asking for ItemSources is what triggers its 170ms sheet sweep, but only ever
        // once, and it answers null by itself while the setting is off - so this is the first frame the
        // section is looked at and no earlier.
        if (ownershipMoved || currencyMoved)
        {
            var sources = plugin.ItemSources;

            Shopping = sources == null
                ? null
                : ShoppingList.Build(
                    CurrencyReader.Read(), sources, ownership, plugin.Outfits, plugin.Storage,
                    plugin.Configuration, plugin.JobFilter, plugin.EquipLocks);
        }

        if (!ownershipMoved)
            return;

        Pressure = DresserPressure.Build(
            ownership, plugin.Outfits, plugin.Storage, plugin.Ownership.ArmoireUpdatedUtc != null);

        Sets = plugin.Ownership.HasDresserData
            ? SetCompletion.InProgress(plugin.Outfits, ownership, plugin.Configuration, plugin.EquipLocks)
            : [];
    }

    /// <summary>
    /// Everything held in one place that the collection accounts for, however it accounts for it.
    /// </summary>
    /// <remarks>
    /// The two halves of the report are merged here rather than drawn as two tiers, which is how this
    /// section used to work and was the one confusing thing in it. Every heading answers "where is
    /// this" - bags, armoury chest, saddlebag, one per retainer - except that the outfit tier answered
    /// "how is this accounted for" instead, so a piece in the armoury chest held only inside a stored
    /// outfit appeared under a heading that never said armoury, below the other headings, reading like
    /// one more place to look.
    ///
    /// One axis, then: the heading is the place, and how the collection accounts for a piece is said on
    /// the row. Nothing is lost, because the row already carried that sentence - it is now the only
    /// thing saying it, and the outfit case is coloured because its second copy is a conditional one.
    /// </remarks>
    public IEnumerable<CarriedPiece> HeldIn(CarryLocation location) =>
        Carried!.AlreadyStored
            .Concat(Carried.OnlyInsideAnOutfit)
            .Where(piece => piece.Location == location)
            .OrderBy(piece => piece.SlotOrder)
            .ThenBy(piece => piece.Name, System.StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Held pieces at retainers, one group per retainer holding something.
    /// </summary>
    /// <remarks>
    /// Ordered by name rather than by how stale each retainer's snapshot is, which is what the Data
    /// tab sorts by: this is a list to find a name in. Why the retainers are named at all rather
    /// than lumped under one heading is the drawing side's argument, at
    /// <c>CollectionView.DrawRetainerGroups</c>.
    /// </remarks>
    public IEnumerable<IGrouping<string, CarriedPiece>> ByRetainer(CarryLocation location) =>
        HeldIn(location)
            .GroupBy(piece => piece.Holder)
            .OrderBy(group => group.Key, System.StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A retainer's name, with somewhere to fall back to.
    /// </summary>
    /// <remarks>
    /// The name comes from the client and is never blank in practice, but a heading reading "Worn by"
    /// would be a puzzle rather than a row.
    /// </remarks>
    public static string Name(string holder) => holder.Length > 0 ? holder : "a retainer";

    /// <summary>The same as a possessive, since "In Ysayle's bags" is the heading that reads.</summary>
    public static string Possessive(string holder) =>
        holder.Length == 0 ? "a retainer's" : holder.EndsWith('s') ? $"{holder}'" : $"{holder}'s";

    /// <summary>
    /// Where the collection's copy is, as the other half of a sentence about two of them.
    /// </summary>
    /// <remarks>
    /// Not <see cref="MissingItems.Describe"/>, which is written for a surface asking "do I have this"
    /// and answers in one place. Here both copies are being named at once, so each needs to say the
    /// Glamour Dresser out loud - "part of a stored outfit set" on its own never mentions the box the
    /// set is sitting in, which is the thing a reader is trying to place.
    /// </remarks>
    public static string SecondCopy(OwnershipSource source) => source switch
    {
        OwnershipSource.Armoire => "There is a second in your Armoire.",
        OwnershipSource.Outfit => "There is a second in your Glamour Dresser, inside a stored outfit set.",
        _ => "There is a second in your Glamour Dresser.",
    };

    private Inputs Capture()
    {
        var configuration = plugin.Configuration;
        return new Inputs(
            configuration.Scope,
            configuration.OutfitOwnership,
            configuration.CountInventoryAndEquipped,
            configuration.CountRetainers,
            configuration.CountRetainerEquipped,
            configuration.HideWeapons,
            configuration.OnlyCurrentJobEquippable,
            configuration.OnlyCurrentGenderEquippable,
            configuration.OnlyCurrentRaceEquippable,
            configuration.ShowAcquisitionSources,
            configuration.ReadyToBuyOutfitsOnly,
            configuration.ExcludeSellBackVendors);
    }

    /// <summary>The settings any section reads, folded into one value so a change is one compare.</summary>
    private readonly record struct Inputs(
        CollectionScope Scope,
        OutfitOwnershipMode Outfits,
        bool CountInventory,
        bool CountRetainers,
        bool CountRetainerEquipped,
        bool HideWeapons,
        bool JobOnly,
        bool GenderOnly,
        bool RaceOnly,
        bool AcquisitionSources,
        bool OutfitsOnly,
        bool ExcludeSellBack);
}
