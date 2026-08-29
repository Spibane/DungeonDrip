using System;
using System.Collections.Generic;
using System.Linq;
using DungeonDrip.Core;
using DungeonDrip.Game;

namespace DungeonDrip.Presentation;

/// <summary>What the duty view shows, described for either backend to draw.</summary>
/// <remarks>
/// The largest presenter, because the duty view is the largest surface: a toolbar, a banner about
/// how far the collection can be trusted, a summary, the role worth chasing, and then either a
/// grouped list of what is missing or - outside a duty - a table of what to queue instead.
///
/// <b>It takes the plugin rather than the dozen values it would otherwise be handed.</b> That is a
/// heavier dependency than the other presenters carry and it is deliberate: this surface reads the
/// report, the duty catalogue, the roulette advice, the outfit sets, the ownership snapshot, the
/// configuration and where the player is, and threading all of that through as arguments would be a
/// worse kind of coupling than naming the one object that already holds it. Nothing here draws, and
/// nothing here mentions either backend, which are the properties that matter.
/// </remarks>
internal static class DutyPresenter
{
    /// <summary>
    /// The window's controls, as buttons showing the state they are in.
    /// </summary>
    /// <remarks>
    /// The middle of the toolbar belongs to the duty view and is absent rather than greyed out in
    /// the collection one. Disabling says "this could work but not right now"; those buttons are
    /// simply not part of what is on screen, and a row of dead controls above a list they cannot
    /// affect reads as broken. Settings stays on the end of both.
    /// </remarks>
    /// <param name="pickerOpen">
    /// Held by the window rather than here, because it is the one piece of state that is about the
    /// window rather than about the gear - and each look keeps its own.
    /// </param>
    public static IReadOnlyList<ToolButtonModel> Toolbar(
        Plugin plugin, bool pickerOpen, Action togglePicker)
    {
        var collectionMode = plugin.Mode == MainWindowMode.Collection;
        var buttons = new List<ToolButtonModel>(6)
        {
            new(collectionMode ? ToolGlyph.ShowingCollection : ToolGlyph.ShowingDuty,
                collectionMode
                    ? "Showing your collection. Click to go back to duty loot."
                    : "Showing duty loot. Click to show your collection.",
                () => plugin.SetMode(collectionMode ? MainWindowMode.Duty : MainWindowMode.Collection)),
        };

        if (!collectionMode)
        {
            buttons.Add(new ToolButtonModel(
                pickerOpen ? ToolGlyph.PickerOpen : ToolGlyph.PickerShut,
                pickerOpen ? "Duty list open. Click to hide it." : "Look a duty up by name.",
                togglePicker));

            buttons.Add(Pin(plugin));
            buttons.Add(Grouping(plugin));
            buttons.Add(ShowOwned(plugin));
        }

        buttons.Add(new ToolButtonModel(ToolGlyph.Settings, "Settings.", plugin.ToggleConfigUi));
        return buttons;
    }

    /// <remarks>
    /// One button showing where letting go lands rather than what letting go does. Unpinning goes
    /// back to tracking the current location, and anywhere without a drop list of its own - a city,
    /// most of the time - that is the roulette advice rather than a duty.
    /// </remarks>
    private static ToolButtonModel Pin(Plugin plugin)
    {
        if (!plugin.IsPinned)
        {
            return new ToolButtonModel(
                ToolGlyph.FollowingPlayer, "Following wherever you are.", () => { }, Enabled: false);
        }

        var toRoulettes = plugin.Duties?.TryGet(plugin.CurrentTerritory, out _) != true;

        return new ToolButtonModel(
            toRoulettes ? ToolGlyph.HoldingDutyOverRoulettes : ToolGlyph.HoldingDuty,
            toRoulettes
                ? "Holding the duty you looked up. Click to go back to the roulette advice."
                : "Holding the duty you looked up. Click to follow wherever you are instead.",
            plugin.Unpin);
    }

    /// <remarks>
    /// One button through three states rather than three buttons: it is one choice, and each
    /// click's destination is named in the tooltip so the cycle is not something to discover.
    /// </remarks>
    private static ToolButtonModel Grouping(Plugin plugin)
    {
        var grouping = plugin.Configuration.Grouping;

        var (icon, tooltip) = grouping switch
        {
            MissingGrouping.Role => (ToolGlyph.GroupedByRole,
                "Grouped by who can roll Need. Click to group by the boss that drops each piece."),
            MissingGrouping.Source => (ToolGlyph.GroupedBySource,
                "Grouped by boss and coffer, as far as the wiki lookup for this duty knows. " +
                "Click to group by equipment slot."),
            _ => (ToolGlyph.GroupedBySlot,
                "Grouped by equipment slot. Click to group by who can roll Need."),
        };

        return new ToolButtonModel(icon, tooltip, () =>
        {
            plugin.Configuration.Grouping = grouping switch
            {
                MissingGrouping.Slot => MissingGrouping.Role,
                MissingGrouping.Role => MissingGrouping.Source,
                _ => MissingGrouping.Slot,
            };

            plugin.Configuration.Save();
            plugin.InvalidateReport();
        });
    }

    private static ToolButtonModel ShowOwned(Plugin plugin)
    {
        var showOwned = plugin.Configuration.ShowOwnedItems;

        return new ToolButtonModel(
            showOwned ? ToolGlyph.ShowingOwned : ToolGlyph.HidingOwned,
            showOwned
                ? "Showing pieces you already have, greyed out. Click to hide them."
                : "Hiding pieces you already have. Click to show them greyed out.",
            () =>
            {
                plugin.Configuration.ShowOwnedItems = !showOwned;
                plugin.Configuration.Save();
            });
    }

    /// <summary>Why there is no list at all, and the one thing that can be done about it.</summary>
    public static IReadOnlyList<Node> NoLootData(Plugin plugin) =>
    [
        new SpacingNode(),
        new TextNode(plugin.LootData.StatusMessage, ColourRole.Warning),
        new TextNode(
            "The dungeon loot list is downloaded rather than shipped with the plugin, so it stays " +
            "current. This only takes a moment the first time.",
            Wrap: true),
        new ButtonNode("Try again", () => plugin.LootData.CheckForUpdates(force: true)),
    ];

    /// <summary>How far the collection snapshot can be trusted, said above everything else.</summary>
    public static IReadOnlyList<Node> FreshnessBanner(OwnershipTracker tracker)
    {
        if (!tracker.HasDresserData)
        {
            return
            [
                new TextNode(
                    "No Glamour Dresser data yet - open a Glamour Dresser once so the plugin can read it.",
                    ColourRole.Warning),
            ];
        }

        var built = new List<Node>(2);

        // Only when something is wrong. How full the box is and when it was last read are the
        // collection view's questions, and repeating them above every duty spent two lines saying
        // that nothing needs doing - which is the state the window is in almost every time it opens.
        if (tracker.IsDresserStale)
        {
            built.Add(new TextNode(
                $"Glamour Dresser snapshot is {Format.Age(tracker.DresserUpdatedUtc!.Value)} old - " +
                "open your dresser to refresh it.",
                ColourRole.Warning));
        }

        if (tracker.ArmoireUpdatedUtc == null)
        {
            built.Add(new TextNode(
                "Armoire has never been read - open it at an inn to include it.", ColourRole.Muted));
        }

        return built;
    }

    /// <summary>How much of this duty is missing, and what the filters are keeping back.</summary>
    public static IReadOnlyList<Node> Summary(DutyReport report)
    {
        if (report.TotalCount == 0)
        {
            return
            [
                new SpacingNode(),
                new TextNode("No glamour-able gear listed for this duty.", ColourRole.Muted),
            ];
        }

        var built = new List<Node>(5)
        {
            new SpacingNode(),
            report.MissingCount == 0
                ? new TextNode($"You have all {report.TotalCount} pieces from this duty.", ColourRole.Good)
                : new TextNode($"Missing {report.MissingCount} of {report.TotalCount} pieces."),
        };

        if (report.HiddenByJobFilter > 0)
        {
            built.Add(new TextNode(
                $"{report.HiddenByJobFilter} hidden by the current-job filter.", ColourRole.Muted));
        }

        if (report.HiddenUnwearable > 0)
        {
            built.Add(new TextNode(
                $"{report.HiddenUnwearable} hidden as this character cannot wear them.", ColourRole.Muted));
        }

        if (report.HiddenWeapons > 0)
            built.Add(new TextNode($"{report.HiddenWeapons} weapons hidden.", ColourRole.Muted));

        return built;
    }

    /// <summary>
    /// Names whichever role still needs the most from this duty.
    /// </summary>
    /// <remarks>
    /// Always counts by role, even when the list is grouped by slot - "what should I chase here" is
    /// a different question from how the list is arranged. Ties are reported as ties rather than
    /// silently picking one, which is the common case on a barely-run duty.
    /// </remarks>
    public static IReadOnlyList<Node> RoleFocus(Plugin plugin, DutyReport report)
    {
        if (!plugin.Configuration.ShowRoleSummary || report.MissingCount == 0)
            return [];

        var byRole = DutyGrouping
            .Build([.. report.Items.Where(item => !item.IsOwned)], MissingGrouping.Role)
            .Select(bucket => (bucket.Group.Label, bucket.Group.Order, Count: bucket.MissingCount))
            .Where(entry => entry.Count > 0)
            .OrderByDescending(entry => entry.Count)
            .ThenBy(entry => entry.Order)
            .ToList();

        if (byRole.Count == 0)
            return [];

        var most = byRole[0].Count;
        var leaders = byRole.Where(entry => entry.Count == most).Select(entry => entry.Label).ToList();

        var tooltip = new List<Node> { new TextNode("Still missing by role:", ColourRole.Muted) };
        foreach (var (label, _, count) in byRole)
            tooltip.Add(new TextNode($"   {count,3}   {label}"));

        if (byRole.Sum(entry => entry.Count) > report.MissingCount)
        {
            tooltip.Add(new SpacingNode());
            tooltip.Add(new TextNode(
                "Shared gear is counted under every role that can roll on it.", ColourRole.Muted));
        }

        return
        [
            new SpacingNode(),
            new RowNode(
                [
                    new TextNode(
                        leaders.Count == 1
                            ? $"Most missing: {leaders[0]} ({most})"
                            : $"Most missing: {string.Join(", ", leaders)} ({most} each)",
                        ColourRole.Focus),
                ],
                tooltip),
        ];
    }
}
