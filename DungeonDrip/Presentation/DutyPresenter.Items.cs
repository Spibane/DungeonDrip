using System.Collections.Generic;
using System.Linq;
using DungeonDrip.Core;
using DungeonDrip.Data;

namespace DungeonDrip.Presentation;

/// <summary>The duty view's two lists: what is missing here, and what to queue instead.</summary>
/// <remarks>
/// Split from the rest of <see cref="DutyPresenter"/> by file rather than by type because they are
/// one presenter with one caller - but the toolbar and the banners are a handful of lines each,
/// where these two are the surface's actual content and the tooltip on a row is the longest single
/// thing the plugin says anywhere.
/// </remarks>
internal static partial class DutyPresenterLists
{
    /// <summary>
    /// The grouped list of what this duty drops, headings and all.
    /// </summary>
    /// <remarks>
    /// The heading's key is the bare group label rather than a prefixed one, which is what the duty
    /// view has always stored - so a heading folded before this was a description stays folded.
    /// </remarks>
    public static IReadOnlyList<Node> Items(Plugin plugin, DutyReport report)
    {
        var configuration = plugin.Configuration;
        var built = new List<Node>();

        // Said once above the list rather than on the one heading it would otherwise appear as. A
        // duty the downloaded dataset covers alone has no per-boss tables behind it, and a list that
        // simply says "not attributed" reads as the grouping being broken.
        if (configuration.Grouping == MissingGrouping.Source && !report.HasOrigins)
        {
            built.Add(new TextNode(
                "Nothing here is attributed to a boss or coffer yet.", ColourRole.Warning));

            built.Add(new TextNode(
                configuration.UseWikiSource
                    ? "Only the wiki says where inside a duty a piece drops, and this duty has no lookup yet."
                    : "Only the wiki says where inside a duty a piece drops, and it is switched off under Data.",
                ColourRole.Muted));

            built.Add(new SpacingNode());
        }

        var visible = report.Items
            .Where(item => configuration.ShowOwnedItems || !item.IsOwned)
            .ToList();

        var groups = DutyGrouping.Build(visible, configuration.Grouping);

        if (groups.Count == 0)
        {
            if (report.TotalCount > 0)
                built.Add(new TextNode("Nothing missing here.", ColourRole.Good));

            return built;
        }

        foreach (var group in groups)
        {
            var missing = group.MissingCount;
            var label = missing > 0 ? $"{group.Group.Label}  ({missing})" : group.Group.Label;

            var rows = new List<Node>(group.Items.Count);
            foreach (var item in group.Items)
                rows.Add(Row(plugin, item));

            built.Add(new CollapsibleNode(group.Group.Label, label, rows));
        }

        return built;
    }

    /// <summary>One piece: its icon, its name, and everything known about getting it.</summary>
    private static RowNode Row(Plugin plugin, ReportItem item)
    {
        var tooltip = new List<Node>
        {
            new TextNode(item.Name),
            new TextNode($"iLvl {item.ItemLevel}", ColourRole.Muted),
            new TextNode(MissingItems.Describe(item.Source)),
            new TextNode(Provenance(item.Provenance), ColourRole.Muted),
        };

        Origins(tooltip, item);
        Acquisitions(tooltip, plugin, item.ItemId);
        OutfitMembership(tooltip, plugin, item.ItemId);

        tooltip.Add(new SpacingNode());
        tooltip.Add(new TextNode("Right-click for options.", ColourRole.Muted));

        return new RowNode(
            [
                new IconNode(item.IconId, 28),
                new TextNode(item.Name, item.IsOwned ? ColourRole.Muted : ColourRole.Default),
            ],
            tooltip,
            new ItemContext(item.ItemId, item.Name));
    }

    /// <summary>
    /// Where inside the duty the piece comes from, when anything knows.
    /// </summary>
    /// <remarks>
    /// Silent when nothing does, which is most of the time. "Drops from: unknown" would be a line
    /// spent on the absence of a line, and worse, it reads as a claim that the piece drops from no
    /// boss - whereas all it means is that the wiki was never asked about this duty.
    /// </remarks>
    private static void Origins(List<Node> tooltip, ReportItem item)
    {
        if (item.Origins.Count == 0)
            return;

        tooltip.Add(new TextNode(
            item.Origins.Count == 1
                ? $"Drops from: {item.Origins[0].Label}"
                : $"Drops from: {string.Join(", ", item.Origins.Select(origin => origin.Label))}",
            ColourRole.Focus));
    }

    /// <summary>
    /// The ways to get a piece that do not involve running the duty being looked at.
    /// </summary>
    /// <remarks>
    /// Worth showing even here, where the piece is listed precisely because it drops in this duty: a
    /// craftable or purchasable alternative is the reason to stop running the duty for it. Silent
    /// when nothing knows, like <see cref="Origins"/>, and for the same reason.
    ///
    /// Uncapped, and safe to be: one line per kind holds this to three, where listing every route
    /// could reach thirty and push the outfit-set section off the bottom of the screen.
    ///
    /// No link out to a reference site here. A tooltip cannot be clicked, so that entry lives on the
    /// row's right-click menu, which this same row already has.
    /// </remarks>
    private static void Acquisitions(List<Node> tooltip, Plugin plugin, uint itemId)
    {
        var acquisitions = plugin.SourcesFor(itemId);
        if (acquisitions is not { Count: > 0 })
            return;

        tooltip.Add(new SpacingNode());
        foreach (var acquisition in acquisitions)
            tooltip.Add(new TextNode(acquisition.Describe(), ColourRole.Muted));
    }

    /// <summary>
    /// Lists the outfit sets a piece belongs to and where each one stands.
    /// </summary>
    /// <remarks>
    /// Three states, not two. A set can be sitting in the dresser with this particular slot empty,
    /// which reads as "not stored" to a check that only asks whether the piece is accounted for -
    /// but the fix is different: top up the set already held rather than hunt the whole outfit.
    /// </remarks>
    private static void OutfitMembership(List<Node> tooltip, Plugin plugin, uint itemId)
    {
        var named = plugin.Outfits.NamedSetsContaining(itemId);
        if (named.Count == 0)
            return;

        var ownership = plugin.Ownership.Current;
        ownership.DresserOutfits.TryGetValue(itemId, out var holdingThisPiece);

        tooltip.Add(new SpacingNode());
        tooltip.Add(new RuleNode());
        tooltip.Add(new TextNode(
            named.Count == 1 ? "Part of 1 outfit set:" : $"Part of {named.Count} outfit sets:",
            ColourRole.Muted));

        foreach (var (setId, name) in named)
        {
            if (holdingThisPiece?.Contains(setId) == true)
                tooltip.Add(new TextNode($"   {name} — stored, includes this piece", ColourRole.Good));
            else if (ownership.StoredOutfits.Contains(setId))
                tooltip.Add(new TextNode($"   {name} — stored, but this slot is empty", ColourRole.Warning));
            else
                tooltip.Add(new TextNode($"   {name} — not stored", ColourRole.Muted));
        }
    }

    private static string Provenance(LootProvenance provenance) => provenance switch
    {
        LootProvenance.Learned => "Not in the downloaded list - recorded because you saw it drop here.",
        LootProvenance.Wiki => "Not in the downloaded list - read off the Console Games Wiki.",
        LootProvenance.Override => "Added by your loot-overrides.json.",
        _ => "Listed by the downloaded dataset.",
    };
}
