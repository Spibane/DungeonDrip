using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using DungeonDrip.Core;
using DungeonDrip.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace DungeonDrip.Windows;

/// <summary>
/// A panel that rides alongside a vendor window, listing the glamour gear it stocks and where you
/// already have each piece.
/// </summary>
/// <remarks>
/// A panel rather than markers drawn onto the shop's own rows, for the same reason
/// <see cref="LootCompanionWindow"/> is one: nothing is written into the addon, so there is nothing
/// for another plugin to collide with. The stronger reason here is scrolling. Vendor lists are long
/// and virtualised - the game recycles a handful of row widgets as you scroll - so an inline glyph
/// has to stay pinned to a moving target, and the failure mode when it slips is a marker sitting
/// beside the wrong item. That is worse than no feature at all: it tells you that you already own
/// something and you walk away from it. A panel cannot misalign, because it never claims to point
/// at a row.
///
/// It lists whatever the vendor is currently showing, not what is currently on screen. Switching a
/// category dropdown re-reads and the panel follows; scrolling does not change it.
/// </remarks>
public sealed unsafe class VendorPanelWindow : Window, IDisposable
{
    private static readonly Vector4 Missing = new(1.00f, 0.85f, 0.40f, 1f);
    private static readonly Vector4 Owned = new(0.55f, 0.55f, 0.55f, 1f);
    private static readonly Vector4 Warning = new(1.00f, 0.71f, 0.20f, 1f);

    private const float GapPixels = 6f;
    private const string CollapsePrefix = "vendor:";

    private readonly Plugin plugin;

    /// <summary>Resolved in DrawConditions and reused for the rest of the frame.</summary>
    private VendorStock? stock;

    /// <summary>Regrouping only happens when the stock or the owned filter actually changes.</summary>
    private readonly List<SlotGroup> groups = [];
    private VendorStock? groupedFrom;
    private bool groupedShowingOwned;
    private bool groupedBySlot;

    /// <summary>
    /// Our own width, measured at the end of the last frame. PreDraw runs before this window's
    /// Begin, so ImGui's current-window queries are not ours to ask there.
    /// </summary>
    private float lastWidth = 240f;

    public VendorPanelWindow(Plugin plugin)
        : base("Already collected###DungeonDripVendorPanel",
               ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse |
               ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing |
               ImGuiWindowFlags.NoNav)
    {
        this.plugin = plugin;
        RespectCloseHotkey = false;
        ShowCloseButton = false;
        DisableWindowSounds = true;
    }

    public void Dispose() { }

    public override bool DrawConditions()
    {
        stock = plugin.Shop.Resolve();

        // A vendor selling nothing wearable gets no panel at all. An empty panel at the potion
        // merchant is noise, and "/dungeondrip shop" is there for anyone wondering whether the
        // feature is alive.
        return stock is { Rows.Count: > 0 };
    }

    public override void PreDraw()
    {
        var name = plugin.Shop.ActiveAddonName;
        if (name == null)
            return;

        var unit = Plugin.GameGui.GetAddonByName<AtkUnitBase>(name);
        if (unit == null)
            return;

        var width = unit->GetScaledWidth(true);
        var height = unit->GetScaledHeight(true);
        var ownWidth = lastWidth;

        var side = plugin.Configuration.VendorPanelSide;
        var toRight = side switch
        {
            LootCompanionSide.Left => false,
            LootCompanionSide.Right => true,
            _ => unit->X + width + GapPixels + ownWidth <= ImGui.GetIO().DisplaySize.X,
        };

        var x = toRight
            ? unit->X + width + GapPixels
            : unit->X - ownWidth - GapPixels;

        // Keep it on screen even if the shop is jammed against an edge.
        x = Math.Clamp(x, 0f, Math.Max(0f, ImGui.GetIO().DisplaySize.X - ownWidth));
        var y = Math.Clamp((float)unit->Y, 0f, Math.Max(0f, ImGui.GetIO().DisplaySize.Y - height));

        Position = new Vector2(x, y);
        PositionCondition = ImGuiCond.Always;
    }

    public override void Draw()
    {
        lastWidth = ImGui.GetWindowWidth();

        if (stock == null)
            return;

        var ownership = plugin.Ownership;
        var stale = ownership.IsDresserStale;

        // Unlike a loot roll, a vendor is a decision you can come back to, so this degrades rather
        // than refusing: it says how much to trust itself and shows the list anyway.
        if (!ownership.HasDresserData)
        {
            ImGui.TextColored(Warning, "No dresser data yet.");
            ImGui.TextColored(Warning, "Open your Glamour Dresser.");
            ImGui.Separator();
        }
        else if (stale)
        {
            var age = DateTime.UtcNow - ownership.DresserUpdatedUtc!.Value;
            ImGui.TextColored(Warning, $"Dresser last read {Format.Age(age)} ago.");
            ImGui.TextColored(Warning, "Open one to be sure.");
            ImGui.Separator();
        }

        var showOwned = plugin.Configuration.VendorShowOwnedItems;
        var bySlot = plugin.Configuration.VendorGroupBySlot;

        Regroup(stock, showOwned, bySlot);

        if (groups.Count == 0)
        {
            ImGui.TextColored(Owned, "Nothing new here.");
            return;
        }

        foreach (var group in groups)
            DrawGroup(group, bySlot, stale);
    }

    private void DrawGroup(SlotGroup group, bool bySlot, bool stale)
    {
        if (!bySlot)
        {
            foreach (var row in group.Rows)
                DrawRow(row, stale);

            return;
        }

        var label = group.Hidden > 0
            ? $"{group.Label}  ({group.NotCollected} of {group.Rows.Count + group.Hidden})"
            : group.NotCollected > 0
                ? $"{group.Label}  ({group.NotCollected})"
                : group.Label;

        var key = CollapsePrefix + group.Label;
        var collapsed = plugin.Configuration.CollapsedGroups.Contains(key);
        ImGui.SetNextItemOpen(!collapsed, ImGuiCond.Appearing);

        var open = ImGui.CollapsingHeader($"{label}###{key}");
        if (open == collapsed)
        {
            if (open)
                plugin.Configuration.CollapsedGroups.Remove(key);
            else
                plugin.Configuration.CollapsedGroups.Add(key);

            plugin.Configuration.Save();
        }

        if (!open)
            return;

        foreach (var row in group.Rows)
            DrawRow(row, stale);

        ImGui.Spacing();
    }

    private static void DrawRow(VendorRow row, bool stale)
    {
        var uncertain = VendorMarkers.IsUncertain(row.Marker, stale);
        var colour = uncertain
            ? Warning
            : row.Marker == VendorMarker.NotCollected
                ? Missing
                : Owned;

        ImGui.AlignTextToFramePadding();

        // Fixed width so the names line up regardless of which glyph each row carries.
        using (Plugin.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
            ImGui.TextColored(colour, Glyph(row.Marker).ToIconString());

        ImGui.SameLine();

        var iconSize = new Vector2(20, 20) * ImGuiHelpers.GlobalScale;
        var icon = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(row.IconId)).GetWrapOrDefault();

        if (icon != null)
            ImGui.Image(icon.Handle, iconSize);
        else
            ImGui.Dummy(iconSize);

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(colour, row.Name);

        if (!ImGui.IsItemHovered())
            return;

        using var tooltip = ImRaii.Tooltip();
        ImGui.Text(row.Name);
        ImGui.TextColored(Owned, $"iLvl {row.ItemLevel}");
        ImGui.TextColored(colour, VendorMarkers.Describe(row.Marker));

        if (uncertain && row.Marker == VendorMarker.NotCollected)
            ImGui.TextColored(Warning, "Your dresser snapshot predates this - check before buying.");
    }

    private static FontAwesomeIcon Glyph(VendorMarker marker) => marker switch
    {
        VendorMarker.Dresser => FontAwesomeIcon.Check,
        VendorMarker.Outfit => FontAwesomeIcon.LayerGroup,
        VendorMarker.Armoire => FontAwesomeIcon.Archive,
        VendorMarker.Inventory => FontAwesomeIcon.Briefcase,
        VendorMarker.NotCollected => FontAwesomeIcon.Star,
        _ => FontAwesomeIcon.Question,
    };

    private void Regroup(VendorStock fresh, bool showOwned, bool bySlot)
    {
        if (ReferenceEquals(groupedFrom, fresh) && groupedShowingOwned == showOwned && groupedBySlot == bySlot)
            return;

        groupedFrom = fresh;
        groupedShowingOwned = showOwned;
        groupedBySlot = bySlot;
        groups.Clear();

        var byLabel = new Dictionary<string, SlotGroup>();

        foreach (var row in fresh.Rows)
        {
            var hidden = !showOwned && row.Marker is not (VendorMarker.NotCollected or VendorMarker.Unknown);

            // One bucket when flat, so the same drawing path serves both layouts.
            var label = bySlot ? row.SlotName : string.Empty;
            var order = bySlot ? row.SlotOrder : 0;

            if (!byLabel.TryGetValue(label, out var group))
            {
                group = new SlotGroup(label, order);
                byLabel[label] = group;
                groups.Add(group);
            }

            if (hidden)
            {
                group.Hidden++;
                continue;
            }

            group.Rows.Add(row);
            if (row.Marker == VendorMarker.NotCollected)
                group.NotCollected++;
        }

        groups.RemoveAll(group => group.Rows.Count == 0);
        groups.Sort((a, b) => a.Order.CompareTo(b.Order));
    }

    private sealed class SlotGroup(string label, int order)
    {
        public string Label { get; } = label;
        public int Order { get; } = order;
        public List<VendorRow> Rows { get; } = [];
        public int NotCollected { get; set; }

        /// <summary>Owned rows filtered out, so the heading can still account for them.</summary>
        public int Hidden { get; set; }
    }
}
