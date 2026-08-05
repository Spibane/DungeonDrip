using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace DungeonDrip.Game;

/// <summary>
/// What the character has to spend: the Currency tab, read as item id to amount.
/// </summary>
/// <remarks>
/// <b>The container is the definition of a currency, and that is the point rather than a shortcut.</b>
/// The shops sheet prices gear in 2,919 different things, of which 2,530 buy exactly one piece - because
/// most of them are not currencies at all but gear being traded up, one base piece for one augmented
/// one. Grouping a shopping list by "whatever the shop charges" therefore produces two and a half
/// thousand groups of one. Filtering on <c>ItemUICategory</c> instead was measured and does not
/// separate them: <em>Miscellany</em> alone holds 246 cost items mixing real currencies, like the
/// Certificate of Import series, with one-off materials.
///
/// Reading the tab settles it with no heuristic at all, follows whatever the game does to it in a
/// patch, and matches what the question means anyway - a thing with no balance is not something a
/// balance can be spent from. Anyone tempted to widen this into a category filter should read the
/// figures above first.
///
/// Deliberately not the <c>Crystals</c> container. Shards and crystals do price some gear - 1,141
/// pieces, which would be one of the largest groups in the list - but nobody thinks of them as money,
/// and excluding the container is what keeps them out.
///
/// Two reads over the one container, matching <see cref="InventoryReader"/>: a dictionary when
/// something needs the balances, and a hash every frame for anything watching them move. Nothing here
/// is cached and none of it can return "no data" - the tab is loaded for as long as the character is.
/// </remarks>
public static unsafe class CurrencyReader
{
    /// <summary>Gil, which sits in this container as an ordinary row like every other currency.</summary>
    public const uint GilItemId = 1;

    /// <summary>
    /// Every currency held, by item id.
    /// </summary>
    /// <remarks>
    /// <b>A currency held at zero is absent rather than present as 0.</b> The game empties the slot
    /// rather than keeping it at zero, so this cannot tell "spent it all" from "never had any" - and a
    /// caller must not read an absence as a reason to hide something it would otherwise show for a
    /// different reason.
    ///
    /// Amounts are <c>long</c> against a <c>Quantity</c> that is narrower, because gil alone reaches
    /// ten figures and a total that silently wrapped would be worse than one that is merely large.
    /// </remarks>
    public static IReadOnlyDictionary<uint, long> Read()
    {
        var held = new Dictionary<uint, long>();

        var manager = InventoryManager.Instance();
        if (manager == null)
            return held;

        var container = manager->GetInventoryContainer(InventoryType.Currency);
        if (container == null || !container->IsLoaded)
            return held;

        for (var slot = 0; slot < container->Size; slot++)
        {
            var item = container->GetInventorySlot(slot);
            if (item == null || item->ItemId == 0 || item->Quantity <= 0)
                continue;

            // Summed rather than assigned. One currency occupying two slots is not something the tab
            // does today, and a balance that silently reported half of itself would be a bad way to
            // find out that changed.
            held[item->ItemId] = held.GetValueOrDefault(item->ItemId) + item->Quantity;
        }

        return held;
    }

    /// <summary>
    /// A hash of the balances, for a caller deciding whether anything has moved.
    /// </summary>
    /// <remarks>
    /// The same FNV-1a shape as <see cref="InventoryReader.Fingerprint"/>, and needed for the same
    /// reason: <see cref="OwnershipTracker.Revision"/> is not a usable trigger here. It moves for the
    /// bags only while the count-inventory setting is on, and never for currencies at all - so anything
    /// derived from a balance has to watch the balance itself.
    /// </remarks>
    public static ulong Fingerprint()
    {
        var hash = 1469598103934665603UL;

        var manager = InventoryManager.Instance();
        if (manager == null)
            return hash;

        var container = manager->GetInventoryContainer(InventoryType.Currency);
        if (container == null || !container->IsLoaded)
            return hash;

        for (var slot = 0; slot < container->Size; slot++)
        {
            var item = container->GetInventorySlot(slot);
            if (item == null || item->ItemId == 0)
                continue;

            hash = (hash * 1099511628211UL) ^ item->ItemId;
            hash = (hash * 1099511628211UL) ^ (uint)item->Quantity;
        }

        return hash;
    }
}
