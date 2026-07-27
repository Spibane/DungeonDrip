using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using DungeonDrip.Core;
using DungeonDrip.Data;
using Lumina.Excel.Sheets;

namespace DungeonDrip.Windows;

public class MissingItemsWindow : Window, IDisposable
{
    private static readonly Vector4 Warning = new(1.00f, 0.71f, 0.20f, 1f);
    private static readonly Vector4 Good = new(0.45f, 0.85f, 0.45f, 1f);
    private static readonly Vector4 Muted = new(0.60f, 0.60f, 0.60f, 1f);
    private static readonly Vector4 Focus = new(0.55f, 0.80f, 1.00f, 1f);

    private readonly Plugin plugin;

    private string dutyFilter = string.Empty;
    private bool pickerOpen;

    public MissingItemsWindow(Plugin plugin)
        : base("Dungeon Drip###DungeonDripMain")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 320),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public void Dispose() { }

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
        var title = report == null ? "Dungeon Drip" : report.Name;
        WindowName = $"{title}###DungeonDripMain";
    }

    public override void Draw()
    {
        DrawToolbar();

        if (plugin.Duties == null)
        {
            ImGui.Spacing();
            ImGui.TextColored(Warning, plugin.LootData.StatusMessage);
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
            ImGui.Spacing();
            ImGui.TextWrapped(
                "No loot data for your current location. Pick a duty above to look one up, or zone into a dungeon.");
            return;
        }

        DrawSummary(report);
        DrawRoleFocus(report);
        ImGui.Separator();
        DrawItems(report);
    }

    private void DrawToolbar()
    {
        if (ImGui.Button(pickerOpen ? "Hide duty list" : "Look up a duty"))
            pickerOpen = !pickerOpen;

        ImGui.SameLine();

        if (plugin.IsPinned)
        {
            if (ImGui.Button("Follow current duty"))
                plugin.Unpin();

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Stop showing the duty you picked and go back to tracking wherever you are.");
        }
        else
        {
            ImGui.BeginDisabled();
            ImGui.Button("Following current duty");
            ImGui.EndDisabled();
        }

        ImGui.SameLine();
        var byRole = plugin.Configuration.Grouping == MissingGrouping.Role;
        if (ImGui.Button(byRole ? "Grouped by role" : "Grouped by slot"))
        {
            plugin.Configuration.Grouping = byRole ? MissingGrouping.Slot : MissingGrouping.Role;
            plugin.Configuration.Save();
            plugin.InvalidateReport();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Switch between equipment slots and who can roll Need, for claiming during a run.");

        ImGui.SameLine();
        var showOwned = plugin.Configuration.ShowOwnedItems;
        if (ImGui.Button(showOwned ? "Owned: shown" : "Owned: hidden"))
        {
            plugin.Configuration.ShowOwnedItems = !showOwned;
            plugin.Configuration.Save();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Show or hide the pieces you already have, greyed out among the missing ones.");

        ImGui.SameLine();
        if (ImGui.Button("Settings"))
            plugin.ToggleConfigUi();
    }

    private void DrawFreshnessBanner()
    {
        var tracker = plugin.Ownership;

        if (!tracker.HasDresserData)
        {
            ImGui.TextColored(Warning,
                "No Glamour Dresser data yet - open a Glamour Dresser once so the plugin can read it.");
            return;
        }

        if (tracker.IsDresserStale)
        {
            var age = DateTime.UtcNow - tracker.DresserUpdatedUtc!.Value;
            ImGui.TextColored(Warning,
                $"Glamour Dresser snapshot is {Describe(age)} old - open your dresser to refresh it.");
        }
        else
        {
            var age = DateTime.UtcNow - tracker.DresserUpdatedUtc!.Value;
            ImGui.TextColored(Muted, $"Dresser: {tracker.DresserSlotsUsed} slots, read {Describe(age)} ago.");
        }

        if (tracker.ArmoireUpdatedUtc == null)
        {
            ImGui.TextColored(Muted, "Armoire has never been read - open it at an inn to include it.");
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

    private static void DrawSummary(DutyReport report)
    {
        ImGui.Spacing();
        if (report.TotalCount == 0)
        {
            ImGui.TextColored(Muted, "No glamour-able gear listed for this duty.");
            return;
        }

        if (report.MissingCount == 0)
            ImGui.TextColored(Good, $"You have all {report.TotalCount} pieces from this duty.");
        else
            ImGui.Text($"Missing {report.MissingCount} of {report.TotalCount} pieces.");

        if (report.HiddenByJobFilter > 0)
            ImGui.TextColored(Muted, $"{report.HiddenByJobFilter} hidden by the current-job filter.");

        if (report.HiddenWeapons > 0)
            ImGui.TextColored(Muted, $"{report.HiddenWeapons} weapons hidden.");
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

        var byRole = report.Items
            .Where(item => !item.IsOwned)
            .GroupBy(item => (item.RoleOrder, item.RoleGroup))
            .Select(group => (Label: group.Key.RoleGroup, group.Key.RoleOrder, Count: group.Count()))
            .OrderByDescending(entry => entry.Count)
            .ThenBy(entry => entry.RoleOrder)
            .ToList();

        if (byRole.Count == 0)
            return;

        var most = byRole[0].Count;
        var leaders = byRole.Where(entry => entry.Count == most).Select(entry => entry.Label).ToList();

        ImGui.Spacing();
        ImGui.TextColored(Focus, leaders.Count == 1
            ? $"Most missing: {leaders[0]} ({most})"
            : $"Most missing: {string.Join(", ", leaders)} ({most} each)");

        if (!ImGui.IsItemHovered())
            return;

        using var tooltip = ImRaii.Tooltip();
        ImGui.TextColored(Muted, "Still missing by role:");
        foreach (var (label, _, count) in byRole)
            ImGui.Text($"   {count,3}   {label}");
    }

    private void DrawItems(DutyReport report)
    {
        var configuration = plugin.Configuration;
        var showOwned = configuration.ShowOwnedItems;
        var byRole = configuration.Grouping == MissingGrouping.Role;

        using var child = ImRaii.Child("itemList", Vector2.Zero, false);
        if (!child.Success)
            return;

        // The report is already sorted by the active grouping, so GroupBy keeps that order.
        var groups = report.Items
            .Where(item => showOwned || !item.IsOwned)
            .GroupBy(item => byRole ? item.RoleGroup : item.SlotName)
            .ToList();

        if (groups.Count == 0)
        {
            if (report.TotalCount > 0)
                ImGui.TextColored(Good, "Nothing missing here.");

            return;
        }

        foreach (var group in groups)
        {
            var missing = group.Count(item => !item.IsOwned);
            var label = missing > 0 ? $"{group.Key}  ({missing})" : group.Key;

            // Restore the remembered state when the window appears, then let clicks win and write
            // whatever the user settles on back to the config.
            var collapsed = configuration.CollapsedGroups.Contains(group.Key);
            ImGui.SetNextItemOpen(!collapsed, ImGuiCond.Appearing);

            var open = ImGui.CollapsingHeader($"{label}###group{group.Key}");
            if (open == collapsed)
            {
                if (open)
                    configuration.CollapsedGroups.Remove(group.Key);
                else
                    configuration.CollapsedGroups.Add(group.Key);

                configuration.Save();
            }

            if (!open)
                continue;

            foreach (var item in group)
                DrawItemRow(item);

            ImGui.Spacing();
        }
    }

    private void DrawItemRow(ReportItem item)
    {
        var iconSize = new Vector2(28, 28) * ImGuiHelpers.GlobalScale;
        var icon = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(item.IconId)).GetWrapOrDefault();

        if (icon != null)
            ImGui.Image(icon.Handle, iconSize);
        else
            ImGui.Dummy(iconSize);

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();

        if (item.IsOwned)
            ImGui.TextColored(Muted, item.Name);
        else
            ImGui.Text(item.Name);

        // Hover and the context popup both bind to the *last* item drawn, so both have to be taken
        // against the name before anything else goes on this row.
        var hovered = ImGui.IsItemHovered();

        using (var context = ImRaii.ContextPopupItem($"##ctx{item.ItemId}"))
        {
            if (context.Success)
            {
                if (ImGui.Selectable("Link in chat"))
                    Plugin.ChatGui.Print(new SeStringBuilder().AddItemLink(item.ItemId, false).Build());

                if (ImGui.Selectable("Copy name"))
                    ImGui.SetClipboardText(item.Name);
            }
        }

        if (!hovered)
            return;

        using var tooltip = ImRaii.Tooltip();
        ImGui.Text(item.Name);
        ImGui.TextColored(Muted, $"iLvl {item.ItemLevel}");
        ImGui.Text(MissingItems.Describe(item.Source));
        ImGui.TextColored(Muted, ProvenanceDescription(item.Provenance));

        DrawOutfitMembership(item.ItemId);

        ImGui.Spacing();
        ImGui.TextColored(Muted, "Right-click for options.");
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
        var sets = plugin.Outfits.SetsContaining(itemId);
        if (sets.Count == 0)
            return;

        var ownership = plugin.Ownership.Current;
        ownership.DresserOutfits.TryGetValue(itemId, out var holdingThisPiece);

        var items = Plugin.DataManager.GetExcelSheet<Item>();
        var named = sets
            .Select(setId => (
                Id: setId,
                Name: items.TryGetRow(setId, out var row) ? row.Name.ExtractText() : $"Outfit {setId}"))
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextColored(Muted, named.Count == 1
            ? "Part of 1 outfit set:"
            : $"Part of {named.Count} outfit sets:");

        foreach (var (setId, name) in named)
        {
            if (holdingThisPiece?.Contains(setId) == true)
                ImGui.TextColored(Good, $"   {name} — stored, includes this piece");
            else if (ownership.StoredOutfits.Contains(setId))
                ImGui.TextColored(Warning, $"   {name} — stored, but this slot is empty");
            else
                ImGui.TextColored(Muted, $"   {name} — not stored");
        }
    }

    private static string ProvenanceDescription(LootProvenance provenance) => provenance switch
    {
        LootProvenance.Learned => "Not in the downloaded list - recorded because you saw it drop here.",
        LootProvenance.Wiki => "Not in the downloaded list - read off the Console Games Wiki.",
        LootProvenance.Override => "Added by your loot-overrides.json.",
        _ => "Listed by the downloaded dataset.",
    };

    private static string Describe(TimeSpan age) => age switch
    {
        { TotalMinutes: < 1 } => "less than a minute",
        { TotalHours: < 1 } => $"{(int)age.TotalMinutes} minute(s)",
        { TotalDays: < 1 } => $"{(int)age.TotalHours} hour(s)",
        _ => $"{(int)age.TotalDays} day(s)",
    };
}
