using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using DungeonDrip.Core;
using DungeonDrip.Data;

namespace DungeonDrip.Windows;

/// <summary>
/// The plugin's main window: what is missing from the duty in view, or the collection as a whole.
/// </summary>
/// <remarks>
/// Two views behind one window rather than two windows, because they are read one after the other -
/// a duty answers "what should I run for", the collection answers "what have I got" - and a second
/// thing to open and position is a cost paid every session for a switch that is one button.
///
/// The spine here is duty-shaped all the way through: the title comes from the report, the window
/// pins a territory, and it opens itself on zoning into one. The collection view is composed in
/// rather than sharing any of that, which is what keeps it from having to pretend to be about a duty.
///
/// Nothing is computed here. The report is built on the framework tick and the collection view keeps
/// its own cache, so a draw is a draw.
/// </remarks>
public class MissingItemsWindow : Window
{
    /// <summary>Heading for pieces no source is known for, and the order that puts it last.</summary>
    private const string UnattributedLabel = "Not attributed to a boss or coffer";

    private const int UnattributedOrder = int.MaxValue;

    private readonly Plugin plugin;
    private readonly CollectionView collection;

    private string dutyFilter = string.Empty;
    private bool pickerOpen;

    public MissingItemsWindow(Plugin plugin)
        : base("Dungeon Drip###DungeonDripMain")
    {
        this.plugin = plugin;
        collection = new CollectionView(plugin);
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 320),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }


    /// <summary>Opens the window with the picker already filtered, for <c>/dungeondrip &lt;name&gt;</c>.</summary>
    public void OpenPicker(string filter)
    {
        dutyFilter = filter;
        pickerOpen = true;
        IsOpen = true;
    }

    /// <summary>
    /// Retitles the window to whatever it is about, keeping the id suffix so ImGui still recognises
    /// it as the same window and remembers where it was put.
    /// </summary>
    public override void PreDraw()
    {
        var report = plugin.Report;
        var title = plugin.Mode == MainWindowMode.Collection
            ? "Collection"
            : report == null ? "Dungeon Drip" : report.Name;

        WindowName = $"{title}###DungeonDripMain";
    }

    /// <summary>
    /// Picks which of the window's states to draw: the collection, a plea for loot data, the
    /// roulette advice, or a duty's list.
    /// </summary>
    /// <remarks>
    /// Four outcomes rather than two, because "no report" splits: outside a duty there is nothing to
    /// report on and the roulette advice is the useful answer, whereas with no dataset at all
    /// nothing can be said and the window has to explain why rather than look broken.
    /// </remarks>
    public override void Draw()
    {
        DrawToolbar();

        // Above the loot-data check on purpose: none of the collection view needs a loot table, and
        // someone on a first run with nothing downloaded yet should still be able to see how full
        // their dresser is.
        if (plugin.Mode == MainWindowMode.Collection)
        {
            DrawFreshnessBanner();
            collection.Draw();
            return;
        }

        if (plugin.Duties == null)
        {
            ImGui.Spacing();
            ImGui.TextColored(Palette.Warning, plugin.LootData.StatusMessage);
            ImGui.TextWrapped(
                "The dungeon loot list is downloaded rather than shipped with the plugin, so it stays " +
                "current. This only takes a moment the first time.");

            if (ImGui.Button("Try again"))
                plugin.LootData.CheckForUpdates(force: true);

            return;
        }

        DrawFreshnessBanner();

        if (pickerOpen)
        {
            DrawDutyPicker();
            ImGui.Separator();
        }

        var report = plugin.Report;
        if (report == null)
        {
            DrawRouletteAdvice();
            return;
        }

        DrawSummary(report);
        DrawRoleFocus(report);
        ImGui.Separator();
        DrawItems(report);
    }

    /// <summary>
    /// The window's controls, as icons for the same reason the vendor panel's are: five buttons
    /// worth of sentences set the window's minimum width, and the list underneath does not need it.
    /// </summary>
    private void DrawToolbar()
    {
        var collectionMode = plugin.Mode == MainWindowMode.Collection;

        if (UiParts.ToolButton(
                "##mode",
                collectionMode ? FontAwesomeIcon.BoxOpen : FontAwesomeIcon.Dungeon,
                collectionMode
                    ? "Showing your collection. Click to go back to duty loot."
                    : "Showing duty loot. Click to show your collection."))
        {
            plugin.SetMode(collectionMode ? MainWindowMode.Duty : MainWindowMode.Collection);
        }

        // The middle of the toolbar belongs to the duty view and is hidden rather than greyed out
        // in the other one. Disabling says "this could work but not right now"; these are simply
        // not part of what is on screen, and a row of dead buttons above a list they cannot affect
        // reads as broken. Settings stays on the end of both.
        if (!collectionMode)
            DrawDutyToolbar();

        ImGui.SameLine();

        if (UiParts.ToolButton("##settings", FontAwesomeIcon.Cog, "Settings."))
            plugin.ToggleConfigUi();
    }

    private void DrawDutyToolbar()
    {
        ImGui.SameLine();

        if (UiParts.ToolButton(
                "##dutyList",
                pickerOpen ? FontAwesomeIcon.SearchMinus : FontAwesomeIcon.Search,
                pickerOpen ? "Duty list open. Click to hide it." : "Look a duty up by name."))
        {
            pickerOpen = !pickerOpen;
        }

        ImGui.SameLine();

        if (plugin.IsPinned)
        {
            // One button, showing where it lands rather than what it does. Unpinning goes back to
            // tracking the current location, and anywhere without a drop list of its own - a
            // city, most of the time - that is the roulette advice.
            var toRoulettes = plugin.Duties?.TryGet(plugin.CurrentTerritory, out _) != true;

            if (UiParts.ToolButton(
                    "##unpin",
                    toRoulettes ? FontAwesomeIcon.Dice : FontAwesomeIcon.Thumbtack,
                    toRoulettes
                        ? "Holding the duty you looked up. Click to go back to the roulette advice."
                        : "Holding the duty you looked up. Click to follow wherever you are instead."))
            {
                plugin.Unpin();
            }
        }
        else
        {
            ImGui.BeginDisabled();
            UiParts.ToolButton(
                "##unpin", FontAwesomeIcon.ThumbtackSlash, "Following wherever you are.");
            ImGui.EndDisabled();
        }

        ImGui.SameLine();

        // One button through three states rather than three buttons: it is one choice, and each
        // click's destination is named in the tooltip so the cycle is not something to discover.
        var grouping = plugin.Configuration.Grouping;
        var (groupingIcon, groupingTooltip) = grouping switch
        {
            MissingGrouping.Role => (FontAwesomeIcon.PeopleGroup,
                "Grouped by who can roll Need. Click to group by the boss that drops each piece."),
            MissingGrouping.Source => (FontAwesomeIcon.Skull,
                "Grouped by boss and coffer, as far as the wiki lookup for this duty knows. " +
                "Click to group by equipment slot."),
            _ => (FontAwesomeIcon.Vest,
                "Grouped by equipment slot. Click to group by who can roll Need."),
        };

        if (UiParts.ToolButton("##grouping", groupingIcon, groupingTooltip))
        {
            plugin.Configuration.Grouping = grouping switch
            {
                MissingGrouping.Slot => MissingGrouping.Role,
                MissingGrouping.Role => MissingGrouping.Source,
                _ => MissingGrouping.Slot,
            };

            plugin.Configuration.Save();
            plugin.InvalidateReport();
        }

        ImGui.SameLine();

        var showOwned = plugin.Configuration.ShowOwnedItems;
        if (UiParts.ToolButton(
                "##owned",
                showOwned ? FontAwesomeIcon.Eye : FontAwesomeIcon.EyeSlash,
                showOwned
                    ? "Showing pieces you already have, greyed out. Click to hide them."
                    : "Hiding pieces you already have. Click to show them greyed out."))
        {
            plugin.Configuration.ShowOwnedItems = !showOwned;
            plugin.Configuration.Save();
        }
    }

    private void DrawFreshnessBanner()
    {
        var tracker = plugin.Ownership;

        if (!tracker.HasDresserData)
        {
            ImGui.TextColored(Palette.Warning,
                "No Glamour Dresser data yet - open a Glamour Dresser once so the plugin can read it.");
            return;
        }

        var age = Format.Age(tracker.DresserUpdatedUtc!.Value);
        if (tracker.IsDresserStale)
            ImGui.TextColored(Palette.Warning, $"Glamour Dresser snapshot is {age} old - open your dresser to refresh it.");
        else
            ImGui.TextColored(Palette.Muted, tracker.DresserSlotCapacity > 0
                ? $"Dresser: {tracker.DresserSlotsUsed} of {tracker.DresserSlotCapacity} slots, read {age} ago."
                : $"Dresser: {tracker.DresserSlotsUsed} slots, read {age} ago.");

        if (tracker.ArmoireUpdatedUtc == null)
        {
            ImGui.TextColored(Palette.Muted, "Armoire has never been read - open it at an inn to include it.");
        }
    }

    private void DrawDutyPicker()
    {
        ImGui.Spacing();
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##dutyFilter", "Search duties by name...", ref dutyFilter, 128);

        using var child = ImRaii.Child("dutyList", new Vector2(-1, 180 * ImGuiHelpers.GlobalScale), true);
        if (!child.Success || plugin.Duties == null)
            return;

        var selected = plugin.SelectedTerritory;
        foreach (var entry in plugin.Duties.Search(dutyFilter))
        {
            var label = entry.Level > 0
                ? $"{entry.Name}  (Lv. {entry.Level}, {entry.ItemCount} pieces)"
                : $"{entry.Name}  ({entry.ItemCount} pieces)";

            if (ImGui.Selectable($"{label}##{entry.TerritoryId}", selected == entry.TerritoryId))
            {
                plugin.PinTerritory(entry.TerritoryId);
                pickerOpen = false;
            }
        }
    }

    /// <summary>
    /// What to queue as, shown while outside any duty with loot data.
    /// </summary>
    /// <remarks>
    /// This is the window's whole content in a city, where the missing list has nothing to say -
    /// so it answers the question that would otherwise need the window opened in a dungeon to ask.
    /// Suppressed without a dresser snapshot, where every piece would read as uncollected and the
    /// table would confidently recommend nonsense.
    /// </remarks>
    private void DrawRouletteAdvice()
    {
        ImGui.Spacing();
        ImGui.TextWrapped(
            "No loot data for your current location. Pick a duty above to look one up, or zone into a dungeon.");

        if (!plugin.Ownership.HasDresserData || plugin.Roulettes.Count == 0)
            return;

        ImGui.Spacing();
        ImGui.TextColored(Palette.Focus, "Or queue a roulette on whichever job stands to gain the most:");
        ImGui.Spacing();

        using var table = ImRaii.Table(
            "roulettes", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp);

        if (!table.Success)
            return;

        ImGui.TableSetupColumn("Roulette", ImGuiTableColumnFlags.WidthStretch, 0.34f);
        ImGui.TableSetupColumn("Queue as", ImGuiTableColumnFlags.WidthStretch, 0.36f);
        ImGui.TableSetupColumn("New", ImGuiTableColumnFlags.WidthStretch, 0.12f);
        ImGui.TableSetupColumn("Chance", ImGuiTableColumnFlags.WidthStretch, 0.18f);
        ImGui.TableHeadersRow();

        foreach (var advice in plugin.Roulettes)
            DrawRouletteRow(advice);
    }

    private static void DrawRouletteRow(RouletteAdvice advice)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();

        // Spans the row so the tooltip is reachable from anywhere along it, not just the name.
        ImGui.Selectable($"{advice.Name}##roulette{advice.Name}", false, ImGuiSelectableFlags.SpanAllColumns);
        var hovered = ImGui.IsItemHovered();

        var best = advice.Roles.Count > 0 ? advice.Roles[0] : null;
        if (best == null || best.Missing == 0)
        {
            ImGui.TableNextColumn();
            ImGui.TextColored(best != null ? Palette.Good : Palette.Muted, best != null
                ? "all collected"
                : advice.DutyCount == 0 ? "no loot data" : $"needs Lv. {advice.RequiredLevel}");

            if (hovered)
                DrawRouletteTooltip(advice);

            return;
        }

        ImGui.TableNextColumn();
        ImGui.TextColored(Palette.Focus, best.Label);

        ImGui.TableNextColumn();
        ImGui.Text($"{best.Missing}");

        ImGui.TableNextColumn();
        ImGui.Text($"{best.Share * 100:0}%");

        if (hovered)
            DrawRouletteTooltip(advice);
    }

    private static void DrawRouletteTooltip(RouletteAdvice advice)
    {
        using var tooltip = ImRaii.Tooltip();

        ImGui.Text(advice.Name);
        ImGui.TextColored(Palette.Muted, advice.DutyCount == advice.PoolCount
            ? $"All {advice.PoolCount} duties in this roulette, counted."
            : $"{advice.DutyCount} of {advice.PoolCount} duties - no loot list for the rest.");

        var worthwhile = advice.Roles.Where(odds => odds.Missing > 0).ToList();
        if (worthwhile.Count == 0)
        {
            ImGui.Spacing();

            // Three ways to have nothing to say, and they want different answers from the reader.
            if (advice.DutyCount == 0)
                ImGui.TextColored(Palette.Muted, "Nothing here has a loot list yet.");
            else if (advice.Roles.Count == 0)
                ImGui.TextColored(Palette.Muted, $"No job of yours is Lv. {advice.RequiredLevel} yet.");
            else
                ImGui.TextColored(Palette.Muted, "Nothing here you have not already collected.");

            return;
        }

        ImGui.Spacing();
        ImGui.TextColored(Palette.Muted, "Uncollected, by role:");
        foreach (var odds in worthwhile)
            ImGui.Text($"   {odds.Missing,4} of {odds.Total,-4}  {odds.Share * 100,3:0}%   {odds.Label}");

        ImGui.Spacing();
        ImGui.TextColored(Palette.Muted, "Odds per piece you can roll on, not per run.");
        ImGui.TextColored(Palette.Muted, "Assumes every duty in the roulette is unlocked.");
    }

    private static void DrawSummary(DutyReport report)
    {
        ImGui.Spacing();
        if (report.TotalCount == 0)
        {
            ImGui.TextColored(Palette.Muted, "No glamour-able gear listed for this duty.");
            return;
        }

        if (report.MissingCount == 0)
            ImGui.TextColored(Palette.Good, $"You have all {report.TotalCount} pieces from this duty.");
        else
            ImGui.Text($"Missing {report.MissingCount} of {report.TotalCount} pieces.");

        if (report.HiddenByJobFilter > 0)
            ImGui.TextColored(Palette.Muted, $"{report.HiddenByJobFilter} hidden by the current-job filter.");

        if (report.HiddenUnwearable > 0)
        {
            ImGui.TextColored(Palette.Muted,
                $"{report.HiddenUnwearable} hidden as this character cannot wear them.");
        }

        if (report.HiddenWeapons > 0)
            ImGui.TextColored(Palette.Muted, $"{report.HiddenWeapons} weapons hidden.");
    }

    /// <summary>
    /// Names whichever role still needs the most from this duty.
    /// </summary>
    /// <remarks>
    /// Always counts by role, even when the list is grouped by slot - "what should I chase here" is
    /// a different question from how the list is arranged. Ties are reported as ties rather than
    /// silently picking one, which is the common case on a barely-run duty.
    /// </remarks>
    private void DrawRoleFocus(DutyReport report)
    {
        if (!plugin.Configuration.ShowRoleSummary || report.MissingCount == 0)
            return;

        var byRole = BuildGroups([.. report.Items.Where(item => !item.IsOwned)], MissingGrouping.Role)
            .Select(bucket => (bucket.Group.Label, bucket.Group.Order, Count: bucket.MissingCount))
            .Where(entry => entry.Count > 0)
            .OrderByDescending(entry => entry.Count)
            .ThenBy(entry => entry.Order)
            .ToList();

        if (byRole.Count == 0)
            return;

        var most = byRole[0].Count;
        var leaders = byRole.Where(entry => entry.Count == most).Select(entry => entry.Label).ToList();

        ImGui.Spacing();
        ImGui.TextColored(Palette.Focus, leaders.Count == 1
            ? $"Most missing: {leaders[0]} ({most})"
            : $"Most missing: {string.Join(", ", leaders)} ({most} each)");

        if (!ImGui.IsItemHovered())
            return;

        using var tooltip = ImRaii.Tooltip();
        ImGui.TextColored(Palette.Muted, "Still missing by role:");
        foreach (var (label, _, count) in byRole)
            ImGui.Text($"   {count,3}   {label}");

        if (byRole.Sum(entry => entry.Count) > report.MissingCount)
        {
            ImGui.Spacing();
            ImGui.TextColored(Palette.Muted, "Shared gear is counted under every role that can roll on it.");
        }
    }

    /// <summary>
    /// Buckets pieces into the headings they should be drawn under.
    /// </summary>
    /// <remarks>
    /// Role view deliberately duplicates. It answers "what can I claim if I queue as this", so a
    /// piece belongs under every heading whose jobs can roll on it: "of Slaying" accessories show
    /// under their own MNK DRG SAM RPR heading and again under Maiming and Striking, and "of Aiming"
    /// under both Physical Ranged and Scouting. Slot view never duplicates and stays the true list,
    /// which is where the counts in the summary come from.
    ///
    /// Broader headings are only folded into narrower ones that some item already created in this
    /// duty. A blind subset rule would conjure a "Melee DPS (DRG)" heading out of a legacy sheet row
    /// and fill it with nothing but shared accessories.
    ///
    /// Source view duplicates for its own reason: a piece in two coffers is genuinely in both, and the
    /// question being asked - what comes out of this - wants it under each. Pieces nothing attributes
    /// share one heading at the end, which on a duty with no wiki lookup is the whole list.
    /// </remarks>
    private static List<Bucket> BuildGroups(IReadOnlyList<ReportItem> items, MissingGrouping grouping)
    {
        var byRole = grouping == MissingGrouping.Role;
        var buckets = new Dictionary<string, Bucket>();

        Bucket Ensure(RoleGroup group)
        {
            if (!buckets.TryGetValue(group.Label, out var bucket))
                buckets[group.Label] = bucket = new Bucket(group);

            return bucket;
        }

        foreach (var item in items)
        {
            switch (grouping)
            {
                case MissingGrouping.Role:
                    foreach (var group in item.RoleGroups)
                        Ensure(group).Add(item);

                    break;

                case MissingGrouping.Source when item.Origins.Count > 0:
                    foreach (var origin in item.Origins)
                        Ensure(new RoleGroup(origin.Order, origin.Label, string.Empty)).Add(item);

                    break;

                case MissingGrouping.Source:
                    Ensure(new RoleGroup(UnattributedOrder, UnattributedLabel, string.Empty)).Add(item);
                    break;

                default:
                    Ensure(new RoleGroup(item.SlotOrder, item.SlotName, string.Empty)).Add(item);
                    break;
            }
        }

        if (byRole)
        {
            foreach (var bucket in buckets.Values.ToList())
            {
                foreach (var item in items)
                {
                    if (item.RoleGroups.Any(group => bucket.Group.IsCoveredBy(group)))
                        bucket.Add(item);
                }
            }
        }

        return [.. buckets.Values.OrderBy(b => b.Group.Order).ThenBy(b => b.Group.Label, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// One role heading's rows, counting its own missing pieces and refusing duplicates.
    /// </summary>
    /// <remarks>
    /// The seen set is what makes the covering pass safe: a piece can reach a heading both as its own
    /// role and by way of a broader heading that covers it, and a heading claiming the same piece
    /// twice would double its count as well as list it twice.
    /// </remarks>
    private sealed class Bucket(RoleGroup group)
    {
        private readonly HashSet<uint> seen = [];

        public RoleGroup Group { get; } = group;

        public List<ReportItem> Items { get; } = [];

        public int MissingCount { get; private set; }

        public void Add(ReportItem item)
        {
            if (!seen.Add(item.ItemId))
                return;

            Items.Add(item);
            if (!item.IsOwned)
                MissingCount++;
        }
    }

    private void DrawItems(DutyReport report)
    {
        var configuration = plugin.Configuration;
        var showOwned = configuration.ShowOwnedItems;

        using var child = ImRaii.Child("itemList", Vector2.Zero, false);
        if (!child.Success)
            return;

        // Said once above the list rather than on the one heading it would otherwise appear as. A duty
        // the downloaded dataset covers alone has no per-boss tables behind it, and a list that simply
        // says "not attributed" reads as the grouping being broken.
        if (configuration.Grouping == MissingGrouping.Source && !report.HasOrigins)
        {
            ImGui.TextColored(Palette.Warning,
                "Nothing here is attributed to a boss or coffer yet.");
            ImGui.TextColored(Palette.Muted,
                configuration.UseWikiSource
                    ? "Only the wiki says where inside a duty a piece drops, and this duty has no lookup yet."
                    : "Only the wiki says where inside a duty a piece drops, and it is switched off under Data.");
            ImGui.Spacing();
        }

        var visible = report.Items.Where(item => showOwned || !item.IsOwned).ToList();
        var groups = BuildGroups(visible, configuration.Grouping);

        if (groups.Count == 0)
        {
            if (report.TotalCount > 0)
                ImGui.TextColored(Palette.Good, "Nothing missing here.");

            return;
        }

        foreach (var group in groups)
        {
            var missing = group.MissingCount;
            var label = missing > 0 ? $"{group.Group.Label}  ({missing})" : group.Group.Label;

            // Restore the remembered state when the window appears, then let clicks win and write
            // whatever the user settles on back to the config.
            var collapsed = configuration.CollapsedGroups.Contains(group.Group.Label);
            ImGui.SetNextItemOpen(!collapsed, ImGuiCond.Appearing);

            var open = ImGui.CollapsingHeader($"{label}###group{group.Group.Label}");
            if (open == collapsed)
            {
                if (open)
                    configuration.CollapsedGroups.Remove(group.Group.Label);
                else
                    configuration.CollapsedGroups.Add(group.Group.Label);

                configuration.Save();
            }

            if (!open)
                continue;

            foreach (var item in group.Items)
                DrawItemRow(item);

            ImGui.Spacing();
        }
    }

    private void DrawItemRow(ReportItem item)
    {
        UiParts.ItemIcon(item.IconId, 28);

        if (item.IsOwned)
            ImGui.TextColored(Palette.Muted, item.Name);
        else
            ImGui.Text(item.Name);

        // Hover and the context popup both bind to the *last* item drawn, so both have to be taken
        // against the name before anything else goes on this row.
        var hovered = ImGui.IsItemHovered();

        UiParts.ItemContextMenu(plugin, item.ItemId, item.Name);

        if (!hovered)
            return;

        using var tooltip = ImRaii.Tooltip();
        ImGui.Text(item.Name);
        ImGui.TextColored(Palette.Muted, $"iLvl {item.ItemLevel}");
        ImGui.Text(MissingItems.Describe(item.Source));
        ImGui.TextColored(Palette.Muted, ProvenanceDescription(item.Provenance));

        DrawOrigins(item);
        DrawAcquisitions(item.ItemId);
        DrawOutfitMembership(item.ItemId);

        ImGui.Spacing();
        ImGui.TextColored(Palette.Muted, "Right-click for options.");
    }

    /// <summary>
    /// The ways to get a piece that do not involve running the duty being looked at.
    /// </summary>
    /// <remarks>
    /// Worth showing even here, where the piece is listed precisely because it drops in this duty: a
    /// craftable or purchasable alternative is the reason to stop running the duty for it. Silent when
    /// nothing knows, like <see cref="DrawOrigins"/> above, and for the same reason - a line saying the
    /// sheets found nothing would be spent on the absence of a line.
    ///
    /// Uncapped, and safe to be: one line per kind holds this to three, where listing every route could
    /// reach thirty and push the outfit-set section off the bottom of the screen.
    ///
    /// No link out to a reference site here. A tooltip cannot be clicked, so that entry lives on the
    /// row's right-click menu, which this same row already has.
    /// </remarks>
    private void DrawAcquisitions(uint itemId)
    {
        var acquisitions = plugin.SourcesFor(itemId);
        if (acquisitions is not { Count: > 0 })
            return;

        ImGui.Spacing();
        foreach (var acquisition in acquisitions)
            ImGui.TextColored(Palette.Muted, acquisition.Describe());
    }

    /// <summary>
    /// Where inside the duty the piece comes from, when anything knows.
    /// </summary>
    /// <remarks>
    /// Silent when nothing does, which is most of the time. "Drops from: unknown" would be a line
    /// spent on the absence of a line, and worse, it reads as a claim that the piece drops from no
    /// boss - whereas all it means is that the wiki was never asked about this duty.
    /// </remarks>
    private static void DrawOrigins(ReportItem item)
    {
        if (item.Origins.Count == 0)
            return;

        ImGui.TextColored(Palette.Focus, item.Origins.Count == 1
            ? $"Drops from: {item.Origins[0].Label}"
            : $"Drops from: {string.Join(", ", item.Origins.Select(origin => origin.Label))}");
    }

    /// <summary>
    /// Lists the outfit sets a piece belongs to and where each one stands.
    /// </summary>
    /// <remarks>
    /// Three states, not two. A set can be sitting in the dresser with this particular slot empty,
    /// which reads as "not stored" to a check that only asks whether the piece is accounted for - but
    /// the fix is different: top up the set already held rather than hunt the whole outfit.
    /// </remarks>
    private void DrawOutfitMembership(uint itemId)
    {
        var named = plugin.Outfits.NamedSetsContaining(itemId);
        if (named.Count == 0)
            return;

        var ownership = plugin.Ownership.Current;
        ownership.DresserOutfits.TryGetValue(itemId, out var holdingThisPiece);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextColored(Palette.Muted, named.Count == 1
            ? "Part of 1 outfit set:"
            : $"Part of {named.Count} outfit sets:");

        foreach (var (setId, name) in named)
        {
            if (holdingThisPiece?.Contains(setId) == true)
                ImGui.TextColored(Palette.Good, $"   {name} — stored, includes this piece");
            else if (ownership.StoredOutfits.Contains(setId))
                ImGui.TextColored(Palette.Warning, $"   {name} — stored, but this slot is empty");
            else
                ImGui.TextColored(Palette.Muted, $"   {name} — not stored");
        }
    }

    private static string ProvenanceDescription(LootProvenance provenance) => provenance switch
    {
        LootProvenance.Learned => "Not in the downloaded list - recorded because you saw it drop here.",
        LootProvenance.Wiki => "Not in the downloaded list - read off the Console Games Wiki.",
        LootProvenance.Override => "Added by your loot-overrides.json.",
        _ => "Listed by the downloaded dataset.",
    };

}
