using System.Collections.Generic;
using DungeonDrip.Core;
using DungeonDrip.Game;

namespace DungeonDrip.Presentation;

/// <summary>What an addon-anchored panel shows, described for either backend to draw.</summary>
/// <remarks>
/// The first surface converted to <see cref="Node"/>, and chosen because it carries every hard part
/// of a gear list in the fewest lines: a marker whose colour and shape are separate decisions, an
/// item icon, a name whose colour says only whether the piece is needed, a multi-line tooltip and a
/// right-click menu.
/// </remarks>
internal static class PanelPresenter
{
    /// <summary>
    /// The filters a panel's list is shown through, as both backends must read them.
    /// </summary>
    /// <remarks>
    /// Built here rather than in a window because these are the same filters the duty list uses and
    /// each has to mean one thing across the plugin. A backend assembling its own would be the
    /// obvious way for "hide weapons" to start meaning something different at a vendor.
    /// </remarks>
    public static ViewOptions Options(Configuration configuration, PanelSettings settings) => new(
        configuration.ShowOwnedItems,
        configuration.OnlyCurrentJobEquippable,
        configuration.OnlyCurrentGenderEquippable,
        configuration.OnlyCurrentRaceEquippable,
        configuration.HideWeapons,
        settings.GroupBySlot);

    /// <summary>
    /// A panel's whole list: the rows, under headings when the panel is grouping by slot.
    /// </summary>
    /// <remarks>
    /// One heading per slot, or one unlabelled group holding everything when it is not grouping -
    /// which is what <see cref="PanelGrouping"/> already produces, so the flat case needs no second
    /// path here. An unlabelled group is emitted as bare rows rather than as a heading with no
    /// name.
    ///
    /// The heading counts what is missing rather than what is listed, and says both when the
    /// filters have hidden some of the group, because a heading reading "(2)" over eight rows is
    /// only puzzling until it says what the two are.
    /// </remarks>
    public static IReadOnlyList<Node> List(
        IReadOnlyList<SlotGroup> groups, bool stale, string? uncertainNote, string collapsePrefix)
    {
        var built = new List<Node>();

        foreach (var group in groups)
        {
            var rows = new List<Node>(group.Rows.Count);
            foreach (var row in group.Rows)
                rows.Add(Row(row, stale, uncertainNote));

            if (group.Label.Length == 0)
            {
                built.AddRange(rows);
                continue;
            }

            built.Add(new CollapsibleNode(collapsePrefix + group.Label, Heading(group), rows));
        }

        return built;
    }

    private static string Heading(SlotGroup group) => group.Hidden > 0
        ? $"{group.Label}  ({group.NotCollected} of {group.Rows.Count + group.Hidden})"
        : group.NotCollected > 0
            ? $"{group.Label}  ({group.NotCollected})"
            : group.Label;

    /// <summary>One gear row: the marker, the icon, the name, its menu and its tooltip.</summary>
    /// <param name="uncertainNote">
    /// What to add to the tooltip when the snapshot cannot be trusted about this piece. Null on a
    /// panel where an ageing snapshot changes nothing about the answer.
    /// </param>
    public static RowNode Row(GearRow row, bool stale, string? uncertainNote)
    {
        var uncertain = CollectionMarkers.IsUncertain(row.Marker, stale);

        var tooltip = new List<Node>
        {
            new TextNode(row.Name),
            new TextNode(CollectionMarkers.Describe(row.Marker, row.OutfitsStored, row.OutfitsTotal)),
        };

        if (uncertain && uncertainNote != null)
            tooltip.Add(new TextNode(uncertainNote, ColourRole.Warning));

        tooltip.Add(new SpacingNode());
        tooltip.Add(new TextNode("Right-click for options.", ColourRole.Muted));

        return new RowNode(
            [
                new GlyphNode(MarkerGlyph.For(row.Marker), MarkerGlyph.ColourFor(row.Marker, uncertain)),
                new IconNode(row.IconId, 20),
                new TextNode(row.Name, MarkerGlyph.NameColourFor(row.Marker)),
            ],
            tooltip,
            new ItemContext(row.ItemId, row.Name));
    }

    /// <summary>
    /// How far the dresser snapshot can be trusted, said above the list rather than on every row.
    /// </summary>
    /// <remarks>
    /// Empty when there is nothing to say, which is the common case - a caveat that appears on every
    /// panel every time is one nobody reads on the panel that means it.
    /// </remarks>
    public static IReadOnlyList<Node> SnapshotWarning(OwnershipTracker ownership, bool stale)
    {
        if (!ownership.HasDresserData)
        {
            return
            [
                new TextNode("No dresser data yet.", ColourRole.Warning),
                new TextNode("Open your Glamour Dresser.", ColourRole.Warning),
                new RuleNode(),
            ];
        }

        if (!stale)
            return [];

        var age = Format.Age(ownership.DresserUpdatedUtc!.Value);

        return
        [
            new TextNode($"Dresser last read {age} ago.", ColourRole.Warning),
            new TextNode("Open one to be sure.", ColourRole.Warning),
            new RuleNode(),
        ];
    }
}
