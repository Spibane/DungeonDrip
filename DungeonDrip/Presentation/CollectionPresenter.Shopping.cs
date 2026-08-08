using System.Collections.Generic;
using DungeonDrip.Core;
using DungeonDrip.Game;

namespace DungeonDrip.Presentation;

/// <summary>The "ready to buy" tab: what each currency held will buy that is not collected yet.</summary>
/// <remarks>
/// One group per currency actually in the Currency tab - see <see cref="CurrencyReader"/> on why the
/// tab is the definition rather than a category filter. A currency held at zero is therefore absent,
/// which is the accepted limit of the arrangement: this answers what can be spent, not what farming
/// something would eventually be worth.
///
/// Gil starts collapsed. It prices roughly five thousand storable pieces, more than every other
/// currency together, so an open gil group would be the section as far as anyone scrolled.
/// </remarks>
internal static class ShoppingPresenter
{
    public static IReadOnlyList<Node> Tab(
        Plugin plugin, CollectionSnapshot snapshot,
        IReadOnlySet<uint> expanded, System.Action<uint> toggleExpanded)
    {
        if (!plugin.Configuration.ShowAcquisitionSources)
        {
            // Named as the setting reads, since that is what has to be found and switched on.
            return
            [
                new TextNode(
                    "Needs \"Say how gear is obtained besides dropping\", under Settings - Data.",
                    ColourRole.Muted),
            ];
        }

        if (snapshot.Shopping == null)
            return [new TextNode("Reading what the shops sell...", ColourRole.Muted)];

        var built = new List<Node> { OutfitsOnly(plugin) };
        built.AddRange(Caveats(plugin));

        if (snapshot.Shopping.IsEmpty)
        {
            built.Add(new TextNode(
                plugin.Configuration.ReadyToBuyOutfitsOnly
                    ? "Nothing your currencies buy towards an outfit set is still missing."
                    : "Nothing your currencies buy is still missing - or there is nothing in the Currency tab yet.",
                ColourRole.Muted));

            return built;
        }

        foreach (var group in snapshot.Shopping.Groups)
            built.Add(Group(plugin, group, expanded.Contains(group.CurrencyItemId), toggleExpanded));

        return built;
    }

    /// <summary>
    /// The section's own view filter, on the section rather than only in Settings.
    /// </summary>
    /// <remarks>
    /// Here because it is a filter on one list and gets flipped while reading it, the same argument
    /// as the duty window's toolbar toggles - a trip to Settings to narrow the list in front of you
    /// is a trip nobody makes twice. It still writes the setting, so it survives a restart.
    /// </remarks>
    private static ToggleNode OutfitsOnly(Plugin plugin) => new(
        "Outfit sets only",
        plugin.Configuration.ReadyToBuyOutfitsOnly,
        value =>
        {
            plugin.Configuration.ReadyToBuyOutfitsOnly = value;
            plugin.Configuration.Save();
        },
        "Hides pieces that are not part of an outfit set.\n\n" +
        "Most priced gear is single pieces - only about one in seven belongs to a set, and for " +
        "gil it is fewer than one in ten - so this is the switch for collecting whole outfits.\n" +
        "Hunting one particular piece is quicker through /dungeondrip item <name>.");

    /// <summary>
    /// Says when the collection behind this list cannot be trusted, before the list rather than after.
    /// </summary>
    /// <remarks>
    /// <b>Every row here is a "not owned" claim, which is the half of a snapshot that goes wrong.</b>
    /// The plugin says so elsewhere and ambers a red x rather than a grey tick for exactly this
    /// reason: glamours are added far more often than removed, so an old snapshot's "owned" almost
    /// always still holds while its "not owned" is what has drifted. This section is nothing but that
    /// claim, so it carries the caveat above the list where the other sections carry theirs below.
    ///
    /// The Armoire line earns its place on a real report: a piece sitting in the Armoire was offered
    /// for sale because the cached read predated it, with a timestamp recent enough to look
    /// trustworthy. Opening the Armoire fixed it, which is what this line asks for.
    /// </remarks>
    private static IReadOnlyList<Node> Caveats(Plugin plugin)
    {
        var tracker = plugin.Ownership;
        var built = new List<Node>(2);

        if (!tracker.HasDresserData)
        {
            built.Add(new TextNode(
                "No Glamour Dresser data yet - open one once, or everything here is listed as missing.",
                ColourRole.Warning));
        }
        else if (tracker.IsDresserStale)
        {
            built.Add(new TextNode(
                $"Dresser snapshot {Format.Age(tracker.DresserUpdatedUtc!.Value)} old - " +
                "anything collected since is still listed.",
                ColourRole.Warning));
        }

        // Its own line rather than folded into the dresser's, because the Armoire is read on a
        // different occasion and can be the stale one on its own.
        if (tracker.ArmoireUpdatedUtc == null)
        {
            built.Add(new TextNode(
                "Armoire never read - anything stored there is still listed. Open it once.",
                ColourRole.Warning));
        }
        else if (plugin.Configuration.Scope != CollectionScope.DresserOnly)
        {
            built.Add(new TextNode(
                $"Armoire read {Format.Age(tracker.ArmoireUpdatedUtc.Value)} ago. " +
                "Open it again if something here is already in it.",
                ColourRole.Muted));
        }

        return built;
    }

    /// <remarks>
    /// The count belongs on the label and must stay off the key, or spending a currency renames the
    /// heading and loses whether it was open.
    ///
    /// Uncoloured. The heading used to go blue when something was affordable, which was a third copy
    /// of what the label already says in words and what the rows already say on the price. Tinting a
    /// heading now reads as "this is a section", which the tabs say instead.
    /// </remarks>
    private static CollapsibleNode Group(
        Plugin plugin, CurrencyGroup group, bool expanded, System.Action<uint> toggleExpanded)
    {
        var affordable = group.Affordable > 0
            ? $"{group.Affordable} affordable"
            : $"{group.Pieces.Count} to save for";

        var shown = expanded
            ? group.Pieces.Count
            : System.Math.Min(group.Pieces.Count, CollectionPresenter.InitialOffersShown);

        var children = new List<Node>();
        for (var index = 0; index < shown; index++)
            children.Add(Offer(group.Pieces[index]));

        if (group.Pieces.Count > CollectionPresenter.InitialOffersShown)
        {
            children.Add(new SpacingNode());
            children.Add(new ButtonNode(
                expanded
                    ? $"Show the cheapest {CollectionPresenter.InitialOffersShown}"
                    : $"Show all {group.Pieces.Count}",
                () => toggleExpanded(group.CurrencyItemId),
                Small: true,
                Key: $"offers{group.CurrencyItemId}"));
        }

        return new CollapsibleNode(
            CollectionPresenter.CollapsePrefix + "currency:" + group.CurrencyItemId,
            $"{group.Name} - {group.Balance:N0}  ({affordable})",
            children,
            DefaultOpen: group.CurrencyItemId != CurrencyReader.GilItemId);
    }

    /// <remarks>
    /// The same row shape the sets tab uses - icon, name, context menu, one trailing muted fact - so
    /// a piece looks the same wherever this view lists it. Only the cost is coloured, and only when
    /// the balance covers it, so the eye lands on what can be bought now.
    /// </remarks>
    private static RowNode Offer(ShoppingPiece piece) => new(
        [new IconNode(piece.IconId, 18), new TextNode(piece.Name)],
        Tooltip: null,
        new ItemContext(piece.ItemId, piece.Name),
        new TextNode(
            $"- {piece.Cost:N0}", piece.Affordable ? ColourRole.Focus : ColourRole.Muted));
}
