using System;
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

    /// <summary>What a filter button adds to its own tooltip, because it is not only this panel's.</summary>
    private const string SharedNote = "Also applies to the duty list.";

    /// <summary>
    /// The shared filter buttons, in the order every panel shows them.
    /// </summary>
    /// <remarks>
    /// Each button shows the state it is in rather than the state it would move to, which makes it
    /// an indicator that can also be pressed - and puts the burden of saying so on the tooltip,
    /// which is why every one of these is two sentences and not a label.
    ///
    /// They write the settings the duty list also reads, not per-panel copies, so
    /// <paramref name="changed"/> has to rebuild that as well as save. Standing at a vendor is where
    /// the wrong filter gets noticed, and it would be a poor answer for it to be the wrong filter
    /// only there.
    /// </remarks>
    /// <param name="hasOwnedRows">
    /// False on a panel whose rows are all one state by construction, where the show-owned button
    /// would sit there doing nothing on either setting. It is left out rather than disabled.
    /// </param>
    public static IReadOnlyList<ToolButtonModel> Filters(
        Configuration configuration, bool hasOwnedRows, Action changed)
    {
        var buttons = new List<ToolButtonModel>(3);

        if (hasOwnedRows)
        {
            buttons.Add(Toggle(
                configuration.ShowOwnedItems,
                ToolGlyph.ShowingOwned, ToolGlyph.HidingOwned,
                "Showing pieces you already have. Click to list only what you are missing.",
                "Hiding pieces you already have. Click to list them too.",
                value => configuration.ShowOwnedItems = value,
                changed));
        }

        buttons.Add(Toggle(
            configuration.OnlyCurrentJobEquippable,
            ToolGlyph.CurrentJobOnly, ToolGlyph.EveryJob,
            "Showing only what your current job can wear. Click to show every job.",
            "Showing gear for every job. Click to show only your current job.",
            value => configuration.OnlyCurrentJobEquippable = value,
            changed));

        buttons.Add(Toggle(
            configuration.HideWeapons,
            ToolGlyph.WeaponsHidden, ToolGlyph.WeaponsListed,
            "Hiding weapons and off-hands. Click to list them.",
            "Listing weapons and off-hands. Click to hide them.",
            value => configuration.HideWeapons = value,
            changed));

        return buttons;
    }

    /// <summary>
    /// The two extra filters a panel beside a storage box carries, for gear already spoken for.
    /// </summary>
    /// <remarks>
    /// Written to the settings both store panels read, not to a pair of their own: armoury gear is a
    /// gearset's and worn gear has to come off whichever box is being stood at, so switching one at
    /// the dresser and finding it switched at the Armoire is the behaviour that needs no explaining.
    ///
    /// Neither carries the shared note, because unlike the three above them these two mean nothing
    /// to the duty list.
    /// </remarks>
    public static IReadOnlyList<ToolButtonModel> StoreFilters(Configuration configuration, Action changed) =>
    [
        Toggle(
            configuration.DresserPanelIncludesArmoury,
            ToolGlyph.ArmouryListed, ToolGlyph.ArmouryHidden,
            "Listing armoury chest gear. Click to hide it.",
            "Hiding armoury chest gear. Click to list it.",
            value => configuration.DresserPanelIncludesArmoury = value,
            changed,
            shared: false),

        Toggle(
            configuration.DresserPanelIncludesEquipped,
            ToolGlyph.EquippedListed, ToolGlyph.EquippedHidden,
            "Listing gear you are wearing. Click to hide it.",
            "Hiding gear you are wearing. Click to list it.",
            value => configuration.DresserPanelIncludesEquipped = value,
            changed,
            shared: false),
    ];

    /// <summary>
    /// One filter button, described from the setting it flips rather than from either of its states.
    /// </summary>
    /// <remarks>
    /// The setting is named once, in the pair of arguments that read it and write it, because every
    /// one of these was previously a local, a button, a write-back and a flag - and four lines with
    /// the setting spelled out twice is a place to mistype which one is being written.
    /// </remarks>
    private static ToolButtonModel Toggle(
        bool state, ToolGlyph onGlyph, ToolGlyph offGlyph, string onTooltip, string offTooltip,
        Action<bool> write, Action changed, bool shared = true)
    {
        var note = shared ? "\n" + SharedNote : string.Empty;

        return new ToolButtonModel(
            state ? onGlyph : offGlyph,
            (state ? onTooltip : offTooltip) + note,
            () =>
            {
                write(!state);
                changed();
            });
    }

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
    /// <param name="row">
    /// How a single row is described, which is the one thing the two kinds of panel disagree about -
    /// see <see cref="StorePanelPresenter.Row"/> for the other answer.
    /// </param>
    public static IReadOnlyList<Node> List(
        IReadOnlyList<SlotGroup> groups, string collapsePrefix, Func<GearRow, Node> row)
    {
        var built = new List<Node>();

        foreach (var group in groups)
        {
            var rows = new List<Node>(group.Rows.Count);
            foreach (var entry in group.Rows)
                rows.Add(row(entry));

            if (group.Label.Length == 0)
            {
                built.AddRange(rows);
                continue;
            }

            built.Add(new CollapsibleNode(collapsePrefix + group.Label, Heading(group), rows));
        }

        return built;
    }

    /// <summary>
    /// What a panel says when its list comes out empty, which is two different things.
    /// </summary>
    /// <remarks>
    /// A list emptied by the filters is the player's own doing and says so; a list that was empty to
    /// begin with is news about the gear. Only the second can be good news, and only on the panels
    /// where having nothing left to store is the point.
    /// </remarks>
    public static IReadOnlyList<Node> Empty(
        bool filtered, string message, string filteredMessage, bool emptyIsGood) => filtered
        ? [new TextNode(filteredMessage, ColourRole.Muted)]
        : [new TextNode(message, emptyIsGood ? ColourRole.Good : ColourRole.Muted)];

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
