using System.Collections.Generic;
using System.Linq;
using DungeonDrip.Core;

namespace DungeonDrip.Presentation;

/// <summary>What to queue, shown while outside any duty with loot data.</summary>
/// <remarks>
/// This is the duty view's whole content in a city, where the missing list has nothing to say - so
/// it answers the question that would otherwise need the window opened in a dungeon to ask.
///
/// Suppressed without a dresser snapshot, where every piece would read as uncollected and the table
/// would confidently recommend nonsense. That is the same rule the loot companion applies and for
/// the same reason: a recommendation is worth less than nothing when the data behind it is absent.
/// </remarks>
internal static class DutyPresenterRoulettes
{
    /// <summary>
    /// The column weights, which both looks divide the available width by.
    /// </summary>
    /// <remarks>
    /// Queue-as is the widest because it holds a job name and is the cell anyone reads first; the
    /// two numbers after it need only their digits. They sum to one, and nothing enforces that -
    /// a table widget renormalises and the hand-written one would simply leave a gap.
    /// </remarks>
    private static readonly float[] Weights = [0.34f, 0.36f, 0.12f, 0.18f];

    private static readonly string[] Headings = ["Roulette", "Queue as", "New", "Chance"];

    public static IReadOnlyList<Node> Advice(Plugin plugin)
    {
        var built = new List<Node>
        {
            new SpacingNode(),
            new TextNode(
                "No loot data for your current location. Pick a duty above to look one up, or zone " +
                "into a dungeon.",
                Wrap: true),
        };

        if (!plugin.Ownership.HasDresserData || plugin.Roulettes.Count == 0)
            return built;

        built.Add(new SpacingNode());
        built.Add(new TextNode(
            "Or queue a roulette on whichever job stands to gain the most:", ColourRole.Focus));
        built.Add(new SpacingNode());

        var rows = new List<RouletteRowModel>(plugin.Roulettes.Count);
        foreach (var advice in plugin.Roulettes)
            rows.Add(Row(advice));

        built.Add(new TableNode(new RouletteTableModel(Headings, Weights, rows)));
        return built;
    }

    /// <remarks>
    /// A roulette with nothing to recommend answers in the second column and stops there. Filling
    /// the remaining two with dashes would put a number-shaped thing where there is no number, and
    /// the reason there is none is exactly what the second column is saying.
    /// </remarks>
    private static RouletteRowModel Row(RouletteAdvice advice)
    {
        var tooltip = Tooltip(advice);
        var best = advice.Roles.Count > 0 ? advice.Roles[0] : null;

        if (best == null || best.Missing == 0)
        {
            return new RouletteRowModel(
                [
                    new TextNode(advice.Name),
                    new TextNode(
                        best != null
                            ? "all collected"
                            : advice.DutyCount == 0 ? "no loot data" : $"needs Lv. {advice.RequiredLevel}",
                        best != null ? ColourRole.Good : ColourRole.Muted),
                ],
                tooltip);
        }

        return new RouletteRowModel(
            [
                new TextNode(advice.Name),
                new TextNode(best.Label, ColourRole.Focus),
                new TextNode($"{best.Missing}"),
                new TextNode($"{best.Share * 100:0}%"),
            ],
            tooltip);
    }

    private static IReadOnlyList<Node> Tooltip(RouletteAdvice advice)
    {
        var tooltip = new List<Node>
        {
            new TextNode(advice.Name),
            new TextNode(
                advice.DutyCount == advice.PoolCount
                    ? $"All {advice.PoolCount} duties in this roulette, counted."
                    : $"{advice.DutyCount} of {advice.PoolCount} duties - no loot list for the rest.",
                ColourRole.Muted),
        };

        var worthwhile = advice.Roles.Where(odds => odds.Missing > 0).ToList();

        if (worthwhile.Count == 0)
        {
            tooltip.Add(new SpacingNode());

            // Three ways to have nothing to say, and they want different answers from the reader.
            tooltip.Add(new TextNode(
                advice.DutyCount == 0
                    ? "Nothing here has a loot list yet."
                    : advice.Roles.Count == 0
                        ? $"No job of yours is Lv. {advice.RequiredLevel} yet."
                        : "Nothing here you have not already collected.",
                ColourRole.Muted));

            return tooltip;
        }

        tooltip.Add(new SpacingNode());
        tooltip.Add(new TextNode("Uncollected, by role:", ColourRole.Muted));

        foreach (var odds in worthwhile)
            tooltip.Add(new TextNode($"   {odds.Missing,4} of {odds.Total,-4}  {odds.Share * 100,3:0}%   {odds.Label}"));

        tooltip.Add(new SpacingNode());
        tooltip.Add(new TextNode("Odds per piece you can roll on, not per run.", ColourRole.Muted));
        tooltip.Add(new TextNode("Assumes every duty in the roulette is unlocked.", ColourRole.Muted));

        return tooltip;
    }
}

/// <summary>One duty the picker can offer, already worded.</summary>
/// <remarks>
/// The label is built here rather than in either look, because it is a sentence about the duty -
/// its level and how many pieces it drops - and a picker that worded it differently in one look
/// would be describing the same duty two ways.
/// </remarks>
internal sealed record DutyPickerEntry(uint TerritoryId, string Label, bool Selected);

/// <summary>The duty picker's contents for a given search.</summary>
/// <remarks>
/// Not a <see cref="Node"/>, for the same reason a toolbar is not: it is a control with state of
/// its own - a text field being typed into and a list being scrolled - and each look has a real
/// widget for that rather than something a renderer could assemble out of rows.
/// </remarks>
internal static class DutyPickerPresenter
{
    public static IReadOnlyList<DutyPickerEntry> Entries(Plugin plugin, string filter)
    {
        if (plugin.Duties == null)
            return [];

        var selected = plugin.SelectedTerritory;
        var entries = new List<DutyPickerEntry>();

        foreach (var entry in plugin.Duties.Search(filter))
        {
            entries.Add(new DutyPickerEntry(
                entry.TerritoryId,
                entry.Level > 0
                    ? $"{entry.Name}  (Lv. {entry.Level}, {entry.ItemCount} pieces)"
                    : $"{entry.Name}  ({entry.ItemCount} pieces)",
                selected == entry.TerritoryId));
        }

        return entries;
    }
}
