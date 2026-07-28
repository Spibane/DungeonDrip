using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
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
public sealed unsafe class VendorPanelWindow : Window
{
    private const string CollapsePrefix = "vendor:";

    /// <summary>Used until the stock has been measured, and as a floor afterwards.</summary>
    private const float FallbackWidth = 260f;

    private const float MinWidth = 180f;
    private const float MinHeight = 120f;

    private const string SharedNote = "Also applies to the duty list.";

    private readonly Plugin plugin;

    /// <summary>Resolved in DrawConditions and reused for the rest of the frame.</summary>
    private IReadOnlyList<VendorRow>? rows;

    /// <summary>Regrouping only happens when the stock or the view options actually change.</summary>
    private readonly List<SlotGroup> groups = [];
    private IReadOnlyList<VendorRow>? groupedFrom;
    private ViewOptions groupedWith;
    private int hiddenByFilter;

    /// <summary>Width the longest name in the current stock needs, measured when it changes.</summary>
    private float contentWidth = FallbackWidth;
    private IReadOnlyList<VendorRow>? measuredFrom;

    /// <summary>The size PreDraw wants, so a size we chose is not mistaken for a drag.</summary>
    private Vector2 appliedSize;

    /// <summary>The size the window actually had last frame.</summary>
    private Vector2 lastSize;

    private bool resizing;

    /// <summary>Frames left for a resize we asked for to land, during which drags are not read.</summary>
    private int applying;

    public VendorPanelWindow(Plugin plugin)
        // Collapsible on purpose: it is pinned where the user cannot move it, so folding it to the
        // title bar is the only way to get it out of the way without turning the feature off.
        // Resizable for the same reason - it cannot be dragged somewhere with more room, so the
        // only way out of a list that overruns the screen is to make the window smaller.
        : base("Vendor Drip###DungeonDripVendorPanel",
               ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav)
    {
        this.plugin = plugin;
        RespectCloseHotkey = false;
        ShowCloseButton = false;
        DisableWindowSounds = true;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(MinWidth, MinHeight),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }


    public override bool DrawConditions()
    {
        rows = plugin.Shop.Resolve();

        // A vendor selling nothing wearable gets no panel at all. An empty panel at the potion
        // merchant is noise, and "/dungeondrip shop" is there for anyone wondering whether the
        // feature is alive.
        if (rows is not { Count: > 0 })
            return false;

        // Measured here rather than in Draw because PreDraw is what needs the answer, and it runs
        // first - measuring later would size each shop's panel to the previous shop's longest name.
        if (!ReferenceEquals(measuredFrom, rows))
        {
            measuredFrom = rows;
            Measure(rows);
        }

        return true;
    }

    public override void PreDraw()
    {
        var name = plugin.Shop.ActiveAddonName;
        if (name == null)
            return;

        var unit = Plugin.GameGui.GetAddonByName<AtkUnitBase>(name);
        if (unit == null)
            return;

        var display = ImGui.GetIO().DisplaySize;

        // Matching the shop's height is what stops a long stock list running off the bottom of the
        // screen, and it happens to line the two windows up. A size the user dragged to wins.
        var configuration = plugin.Configuration;
        var desired = configuration is { VendorPanelWidth: > 0, VendorPanelHeight: > 0 }
            ? new Vector2(configuration.VendorPanelWidth, configuration.VendorPanelHeight)
            : new Vector2(contentWidth, unit->GetScaledHeight(true));

        desired.X = Math.Clamp(desired.X, MinWidth, display.X);
        desired.Y = Math.Clamp(desired.Y, MinHeight, display.Y);

        // Appearing alone is not enough: it only lands while the window is coming up, so pressing
        // reset - or walking to a taller vendor - did nothing visible until the panel happened to
        // close and open again. Whenever what we want differs from what is on screen, insist on it.
        //
        // Not while the user has hold of the resize grip: mid-drag their size is not saved yet, so
        // the tracked size still differs and insisting would snap the window out from under them on
        // every frame of the gesture.
        var moved = lastSize != Vector2.Zero && Vector2.Distance(desired, lastSize) > 1f;
        var theirs = resizing || ImGui.IsMouseDown(ImGuiMouseButton.Left);

        Size = desired;
        appliedSize = desired;

        if (moved && !theirs)
        {
            SizeCondition = ImGuiCond.Always;

            // Frames to let our own resize land before user drags are watched for again. Bounded
            // so a size that can never settle exactly cannot wedge the capture off for good.
            applying = 5;
        }
        else
        {
            SizeCondition = ImGuiCond.Appearing;
        }

        Position = AddonAnchor.Beside(unit, configuration.VendorPanelSide, desired);
        PositionCondition = ImGuiCond.Always;
    }

    public override void Draw()
    {
        RememberUserResize();

        if (rows == null)
            return;

        if (DrawToolbar())
        {
            plugin.Configuration.Save();

            // These are shared with the duty list, so flipping one here has to rebuild that too.
            plugin.InvalidateReport();
        }

        var ownership = plugin.Ownership;
        var stale = ownership.IsDresserStale;

        // Unlike a loot roll, a vendor is a decision you can come back to, so this degrades rather
        // than refusing: it says how much to trust itself and shows the list anyway.
        if (!ownership.HasDresserData)
        {
            ImGui.TextColored(Palette.Warning, "No dresser data yet.");
            ImGui.TextColored(Palette.Warning, "Open your Glamour Dresser.");
            ImGui.Separator();
        }
        else if (stale)
        {
            var age = Format.Age(ownership.DresserUpdatedUtc!.Value);
            ImGui.TextColored(Palette.Warning, $"Dresser last read {age} ago.");
            ImGui.TextColored(Palette.Warning, "Open one to be sure.");
            ImGui.Separator();
        }

        // The list filters are deliberately the same ones the duty window uses, so "only what my job
        // can wear" means one thing across the plugin rather than two.
        var options = new ViewOptions(
            plugin.Configuration.ShowOwnedItems,
            plugin.Configuration.OnlyCurrentJobEquippable,
            plugin.Configuration.HideWeapons,
            plugin.Configuration.VendorGroupBySlot);

        Regroup(rows, options);

        if (groups.Count == 0)
        {
            ImGui.TextColored(Palette.Muted, hiddenByFilter > 0
                ? "Nothing left after your filters."
                : "Nothing new here.");

            return;
        }

        // Everything above stays put; only the list scrolls, so the warning about stale data and
        // the filter buttons cannot be scrolled out of reach.
        using var list = ImRaii.Child("vendorList", Vector2.Zero, false);
        if (!list.Success)
            return;

        foreach (var group in groups)
            DrawGroup(group, options.GroupBySlot, stale);
    }

    /// <summary>
    /// Stores a size the user dragged to, so it is not thrown away the next time a shop opens.
    /// </summary>
    /// <remarks>
    /// Saved on mouse release rather than per frame, because a drag changes the size on every one
    /// of them and writing the config file sixty times a second to record an in-progress gesture
    /// would be absurd.
    ///
    /// Telling our resizes from theirs needs a latch, not just a comparison. This runs before the
    /// toolbar draws, so mistaking one of ours for a drag does not merely store a stray size - it
    /// puts a custom size back in the config that the very same frame's toolbar then reads, which
    /// is how pressing reset used to leave its own button behind: the window really had reverted,
    /// and the button really was reporting a custom size, because this had just written one.
    /// </remarks>
    private void RememberUserResize()
    {
        var size = ImGui.GetWindowSize();
        var settled = Vector2.Distance(size, appliedSize) < 1f;

        // A resize PreDraw asked for. Wait for it to arrive without reading anything into it.
        if (applying > 0)
        {
            applying = settled ? 0 : applying - 1;
            lastSize = size;
            resizing = false;
            return;
        }

        if (size != lastSize)
        {
            lastSize = size;
            resizing = true;
        }

        if (!resizing || ImGui.IsMouseDown(ImGuiMouseButton.Left))
            return;

        resizing = false;

        if (settled)
            return;

        plugin.Configuration.VendorPanelWidth = size.X;
        plugin.Configuration.VendorPanelHeight = size.Y;
        plugin.Configuration.Save();
    }

    /// <summary>
    /// The list filters, as buttons rather than a trip to Settings.
    /// </summary>
    /// <remarks>
    /// They write the shared settings rather than vendor-only copies, which is the point: standing
    /// at a vendor is exactly when you find out you wanted "everything" instead of "my job", and
    /// having to remember which of two similarly-named toggles governs this window is the confusion
    /// the settings tabs were reorganised to end. The button shows the state it is in, not the state
    /// it would move to, so it reads as an indicator you can also press.
    /// </remarks>
    private bool DrawToolbar()
    {
        var configuration = plugin.Configuration;
        var changed = false;

        var showOwned = configuration.ShowOwnedItems;
        if (ImGuiComponents.IconButton("##vendorOwned", showOwned ? FontAwesomeIcon.Eye : FontAwesomeIcon.EyeSlash))
        {
            configuration.ShowOwnedItems = !showOwned;
            changed = true;
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(showOwned
                ? "Showing pieces you already have. Click to list only what you are missing.\n" + SharedNote
                : "Hiding pieces you already have. Click to list them too.\n" + SharedNote);
        }

        ImGui.SameLine();

        var jobOnly = configuration.OnlyCurrentJobEquippable;
        if (ImGuiComponents.IconButton("##vendorJob", jobOnly ? FontAwesomeIcon.User : FontAwesomeIcon.Users))
        {
            configuration.OnlyCurrentJobEquippable = !jobOnly;
            changed = true;
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(jobOnly
                ? "Showing only what your current job can wear. Click to show every job.\n" + SharedNote
                : "Showing gear for every job. Click to show only your current job.\n" + SharedNote);
        }

        ImGui.SameLine();

        var hideWeapons = configuration.HideWeapons;
        if (ImGuiComponents.IconButton("##vendorWeapons", hideWeapons ? FontAwesomeIcon.Ban : FontAwesomeIcon.Khanda))
        {
            configuration.HideWeapons = !hideWeapons;
            changed = true;
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(hideWeapons
                ? "Hiding weapons and off-hands. Click to list them.\n" + SharedNote
                : "Listing weapons and off-hands. Click to hide them.\n" + SharedNote);
        }

        // Only offered once there is something to undo, so the default toolbar stays three buttons
        // wide. Without it a drag is a one-way door - nothing else restores the tracking.
        if (configuration is { VendorPanelWidth: > 0, VendorPanelHeight: > 0 })
        {
            ImGui.SameLine();

            if (ImGuiComponents.IconButton("##vendorSize", FontAwesomeIcon.ArrowsAltV))
            {
                configuration.VendorPanelWidth = 0;
                configuration.VendorPanelHeight = 0;
                changed = true;
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Using a size you set. Click to match the vendor window again.");
        }

        ImGui.Separator();
        return changed;
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

        // The glyph carries the whole state; the name only says whether you need the thing. That
        // split is why the name is left at the theme's own colour rather than tinted - plain white
        // reads as "this is the list", and any tint on it competes with the glyph for meaning.
        var glyphColour = row.Marker switch
        {
            VendorMarker.OutfitComplete => Palette.Good,
            VendorMarker.NotCollected => uncertain ? Palette.Warning : Palette.NotOwned,
            VendorMarker.Unknown => Palette.Warning,
            _ => Palette.Muted,
        };

        ImGui.AlignTextToFramePadding();

        // Fixed width so the names line up regardless of which glyph each row carries.
        using (Plugin.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
            ImGui.TextColored(glyphColour, Glyph(row.Marker).ToIconString());

        ImGui.SameLine();
        UiParts.ItemIcon(row.IconId, 20);

        if (row.Marker == VendorMarker.NotCollected)
            ImGui.Text(row.Name);
        else if (row.Marker == VendorMarker.Unknown)
            ImGui.TextColored(Palette.Warning, row.Name);
        else
            ImGui.TextColored(Palette.Muted, row.Name);

        if (!ImGui.IsItemHovered())
            return;

        using var tooltip = ImRaii.Tooltip();
        ImGui.Text(row.Name);
        ImGui.TextColored(Palette.Muted, $"iLvl {row.ItemLevel}");
        ImGui.TextColored(glyphColour, VendorMarkers.Describe(row.Marker));

        if (uncertain && row.Marker == VendorMarker.NotCollected)
            ImGui.TextColored(Palette.Warning, "Your dresser snapshot predates this - check before buying.");
    }

    /// <remarks>
    /// The star marks a finished outfit and nothing else. It used to mark "not collected", which
    /// read backwards - a star is a reward everywhere else in the game, so putting it against the
    /// pieces you are missing made the panel look like it was congratulating you on the gaps.
    /// </remarks>
    private static FontAwesomeIcon Glyph(VendorMarker marker) => marker switch
    {
        VendorMarker.Dresser => FontAwesomeIcon.Check,
        VendorMarker.Outfit => FontAwesomeIcon.LayerGroup,
        VendorMarker.OutfitComplete => FontAwesomeIcon.Star,
        VendorMarker.Armoire => FontAwesomeIcon.Archive,
        VendorMarker.Inventory => FontAwesomeIcon.Briefcase,
        VendorMarker.NotCollected => FontAwesomeIcon.Times,
        _ => FontAwesomeIcon.Question,
    };

    private void Regroup(IReadOnlyList<VendorRow> fresh, ViewOptions options)
    {
        if (ReferenceEquals(groupedFrom, fresh) && groupedWith == options)
            return;

        groupedFrom = fresh;
        groupedWith = options;
        groups.Clear();
        hiddenByFilter = 0;

        var byLabel = new Dictionary<string, SlotGroup>();

        foreach (var row in fresh)
        {
            // Dropped outright rather than counted against a heading: these say nothing about your
            // collection, only that you asked not to see them.
            if (options.HideWeapons && EquipSlots.IsWeaponSlot(row.SlotOrder))
            {
                hiddenByFilter++;
                continue;
            }

            if (options.JobOnly && !row.JobEquippable)
            {
                hiddenByFilter++;
                continue;
            }

            var hidden = !options.ShowOwned && row.IsOwned;

            // One bucket when flat, so the same drawing path serves both layouts.
            var label = options.GroupBySlot ? row.SlotName : string.Empty;
            var order = options.GroupBySlot ? row.SlotOrder : 0;

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

    /// <summary>
    /// How wide the panel has to be for the longest item name to fit.
    /// </summary>
    /// <remarks>
    /// The window used to size itself to its content, which is what let a long list run off the
    /// bottom of the screen. Fixing the height means fixing the width too, and a width guessed at
    /// a constant would clip exactly the names that are hardest to recognise from their first half.
    /// Measured here rather than per frame because it only changes when the stock does.
    /// </remarks>
    private void Measure(IReadOnlyList<VendorRow> fresh)
    {
        var widest = 0f;
        foreach (var row in fresh)
            widest = Math.Max(widest, ImGui.CalcTextSize(row.Name).X);

        var style = ImGui.GetStyle();

        // Marker, item icon and the spacing around them, plus room for the scrollbar the list will
        // have whenever the stock is taller than the shop window.
        var furniture = (44f * ImGuiHelpers.GlobalScale) +
                        (style.ItemSpacing.X * 2) +
                        (style.WindowPadding.X * 2) +
                        style.ScrollbarSize;

        contentWidth = Math.Max(FallbackWidth, widest + furniture);
    }

    /// <summary>Everything that changes the shape of the list, compared by value in one go.</summary>
    private readonly record struct ViewOptions(
        bool ShowOwned,
        bool JobOnly,
        bool HideWeapons,
        bool GroupBySlot);

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
