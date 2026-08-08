using System.Collections.Generic;
using DungeonDrip.Core;

namespace DungeonDrip.Presentation;

/// <summary>What the collection view shows, described for either backend to draw.</summary>
/// <remarks>
/// Three tabs, three questions, and the order is what each one costs to act on: an outfit part way
/// done is a duty to run, gear a held currency covers is a walk to a counter, and the dresser is
/// housekeeping.
///
/// Nothing here computes anything. Every tab sweeps the whole collection, which is far too much to
/// do per frame, so <see cref="CollectionSnapshot"/> holds the reports and rebuilds them only when
/// something behind them has moved - and describing a tab is a walk over what it already found.
///
/// The tab strip itself is not described. It has state of its own, like the duty picker and the
/// toolbars, and each look has a real widget for it.
/// </remarks>
internal static class CollectionPresenter
{
    /// <summary>
    /// Prefix on the remembered heading state, so nothing here collides with a slot or role heading
    /// from the duty list, which share the same store.
    /// </summary>
    public const string CollapsePrefix = "collection:";

    /// <summary>Above this share of the box, the count is worth colouring.</summary>
    private const float CrowdedAt = 0.9f;

    /// <summary>Sets listed before the "show all" toggle earns its place.</summary>
    public const int InitialSetsShown = 25;

    /// <summary>
    /// Pieces listed per currency before the same toggle earns its place.
    /// </summary>
    /// <remarks>
    /// Lower than the set cap because there can be a dozen currency groups where there is one set
    /// list, and because Wolf Marks alone buy 779 pieces. The plugin has no list clipper anywhere,
    /// so a cap with a toggle is the established answer to a long list rather than a new mechanism.
    /// </remarks>
    public const int InitialOffersShown = 10;

    /// <summary>
    /// Outfit sets part way through, closest to done first.
    /// </summary>
    /// <remarks>
    /// One count per row. Reporting the dresser copy's filled slots beside it was tried and read as
    /// an error rather than as extra information - the two are out of different totals and only one
    /// of them obeys the storage scope, so they disagreed for correct reasons that no row has space
    /// to explain.
    /// </remarks>
    public static IReadOnlyList<Node> Sets(
        Plugin plugin, CollectionSnapshot snapshot, bool showAll, System.Action toggleShowAll)
    {
        if (!plugin.Ownership.HasDresserData)
        {
            return
            [
                new TextNode(
                    "No Glamour Dresser data yet - open a dresser once so this can be answered.",
                    ColourRole.Muted),
            ];
        }

        if (snapshot.Sets.Count == 0)
        {
            return
            [
                new TextNode(
                    "No outfit set is part way done - every set is either finished or untouched.",
                    ColourRole.Muted),
            ];
        }

        var built = new List<Node>();
        var shown = showAll ? snapshot.Sets.Count : System.Math.Min(snapshot.Sets.Count, InitialSetsShown);

        for (var index = 0; index < shown; index++)
        {
            var standing = snapshot.Sets[index];
            var missing = new List<Node>();

            foreach (var piece in standing.Missing)
                missing.Add(MissingPiece(plugin, piece));

            built.Add(new TreeNode(
                $"set{standing.SetId}",
                standing.Name,
                missing,
                standing.IconId,
                new TextNode($"{standing.Owned} of {standing.Total}", ColourRole.Muted)));
        }

        if (snapshot.Sets.Count <= InitialSetsShown)
            return built;

        built.Add(new SpacingNode());
        built.Add(new ButtonNode(
            showAll
                ? $"Show the closest {InitialSetsShown}"
                : $"Show all {snapshot.Sets.Count}",
            toggleShowAll,
            Small: true,
            Key: "setsAll"));

        return built;
    }

    /// <remarks>
    /// One line, so one route: the duty if there is one, otherwise the best non-duty route. The duty
    /// wins because this view is about finishing a set and a duty can be queued for from here, where
    /// a vendor cannot. Both being absent leaves the row bare, which is the honest state - see the
    /// drop index and the source index on why neither can say "nowhere".
    /// </remarks>
    private static RowNode MissingPiece(Plugin plugin, SetPieceState piece)
    {
        var drops = plugin.Drops?.For(piece.ItemId);

        var trailing = drops is { Count: > 0 }
            ? new TextNode(
                drops[0].Level > 0
                    ? $"- {drops[0].DutyName} (Lv. {drops[0].Level})"
                    : $"- {drops[0].DutyName}",
                ColourRole.Muted)
            : plugin.SourcesFor(piece.ItemId) is { Count: > 0 } acquisitions
                ? new TextNode($"- {acquisitions[0].Describe()}", ColourRole.Muted)
                : null;

        return new RowNode(
            [new IconNode(piece.IconId, 18), new TextNode(piece.Name)],
            Tooltip: null,
            new ItemContext(piece.ItemId, piece.Name),
            trailing);
    }

    /// <summary>
    /// How full the box is, with a denominator only when there is an honest one to give.
    /// </summary>
    /// <remarks>
    /// The capacity read off the client is the box's structural size, which is not the same as how
    /// many slots a given character has actually unlocked, and the client offers no way to tell. So
    /// a bare count is the claim that can always be stood behind; the "of N" is worded as the most
    /// the box can hold rather than as the room left.
    ///
    /// A stale snapshot only ever undercounts, because a dresser is added to far more often than it
    /// is taken from, so the number is prefixed rather than suppressed.
    /// </remarks>
    public static IReadOnlyList<Node> Occupancy(DresserPressureReport report, bool stale)
    {
        var prefix = stale ? "At least " : string.Empty;

        if (report.Capacity <= 0)
            return [new TextNode($"{prefix}{report.Used} slots used.", ColourRole.Muted)];

        var crowded = report.Used >= report.Capacity * CrowdedAt;

        return
        [
            new TextNode(
                $"{prefix}{report.Used} slots used, of the {report.Capacity} the box holds.",
                crowded ? ColourRole.Warning : ColourRole.Muted),
            new ProgressNode((float)report.Used / report.Capacity),
        ];
    }
}
