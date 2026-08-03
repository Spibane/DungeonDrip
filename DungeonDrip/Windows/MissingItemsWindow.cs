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

public class MissingItemsWindow : Window
{
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

    public override void PreDraw()
    {
        var report = plugin.Report;
        var title = plugin.Mode == MainWindowMode.Collection
            ? "Collection"
            : report == null ? "Dungeon Drip" : report.Name;

        WindowName = $"{title}###DungeonDripMain";
    }

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
            // tracking wherever you are, and standing anywhere without a drop list of its own - a
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

        var byRole = plugin.Configuration.Grouping == MissingGrouping.Role;
        if (UiParts.ToolButton(
                "##grouping",
                byRole ? FontAwesomeIcon.PeopleGroup : FontAwesomeIcon.Vest,
                byRole
                    ? "Grouped by who can roll Need. Click to group by equipment slot."
                    : "Grouped by equipment slot. Click to group by who can roll Need."))
        {
            plugin.Configuration.Grouping = byRole ? MissingGrouping.Slot : MissingGrouping.Role;
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
    /// What to queue as, shown while you are not standing in a duty we have loot for.
    /// </summary>
    /// <remarks>
    /// This is the window's whole content in a city, where the missing list has nothing to say -
    /// so it answers the question you would otherwise have to open the window in a dungeon to ask.
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

        if (report.HiddenWeapons > 0)
            ImGui.TextColored(Palette.Muted, $"{report.HiddenWeapons} weapons hidden.");
    }

    /// <summary>
    /// Names whichever role still needs the most from this duty.
    /// </summary>
    /// <remarks>
    /// Always counts by role, even when the list is grouped by slot - "what should I chase here" is
    /// a different question from how you want the list arranged. Ties are reported as ties rather
    /// than silently picking one, which is the common case on a duty you have barely run.
    /// </remarks>
    private void DrawRoleFocus(DutyReport report)
    {
        if (!plugin.Configuration.ShowRoleSummary || report.MissingCount == 0)
            return;

        var byRole = BuildGroups([.. report.Items.Where(item => !item.IsOwned)], true)
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
    /// </remarks>
    private static List<Bucket> BuildGroups(IReadOnlyList<ReportItem> items, bool byRole)
    {
        var buckets = new Dictionary<string, Bucket>();

        Bucket Ensure(RoleGroup group)
        {
            if (!buckets.TryGetValue(group.Label, out var bucket))
                buckets[group.Label] = bucket = new Bucket(group);

            return bucket;
        }

        foreach (var item in items)
        {
            if (byRole)
            {
                foreach (var group in item.RoleGroups)
                    Ensure(group).Add(item);
            }
            else
            {
                Ensure(new RoleGroup(item.SlotOrder, item.SlotName, string.Empty)).Add(item);
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
        var byRole = configuration.Grouping == MissingGrouping.Role;

        using var child = ImRaii.Child("itemList", Vector2.Zero, false);
        if (!child.Success)
            return;

        var visible = report.Items.Where(item => showOwned || !item.IsOwned).ToList();
        var groups = BuildGroups(visible, byRole);

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

        DrawOutfitMembership(item.ItemId);

        ImGui.Spacing();
        ImGui.TextColored(Palette.Muted, "Right-click for options.");
    }

    /// <summary>
    /// Lists the outfit sets a piece belongs to and where each one stands.
    /// </summary>
    /// <remarks>
    /// Three states, not two. A set can be sitting in your dresser with this particular slot empty,
    /// which reads as "not stored" if you only check whether the piece is accounted for - but the
    /// fix is different: you top up the set you already have rather than hunting the whole outfit.
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
