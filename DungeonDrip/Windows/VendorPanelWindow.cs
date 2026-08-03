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
/// A panel rather than markers on the shop's own rows. Vendor lists are long and virtualised - the
/// game recycles a handful of row widgets as you scroll - so an inline glyph has to stay pinned to a
/// moving target, and a marker that slips onto the wrong item is worse than no feature at all. A
/// panel cannot misalign because it never claims to point at a row.
///
/// It lists what the vendor is currently showing, not what is on screen: a category change re-reads,
/// scrolling does not.
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

    // Collapsible and resizable because it cannot be dragged: folding or shrinking it is the only
    // way to get it out of the way short of switching the feature off.
    public VendorPanelWindow(Plugin plugin)
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

        // Here rather than in Draw because PreDraw consumes it and runs first; measuring later would
        // size each shop's panel to the previous shop's longest name.
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
        var scale = ImGuiHelpers.GlobalScale;

        // Matching the shop's height keeps a long list on screen, and lines the two windows up.
        // A size the user dragged to wins.
        var configuration = plugin.Configuration;
        var desired = configuration is { VendorPanelWidth: > 0, VendorPanelHeight: > 0 }
            ? new Vector2(configuration.VendorPanelWidth, configuration.VendorPanelHeight)
            : new Vector2(contentWidth, unit->GetScaledHeight(true));

        // Against the constraints as ImGui will enforce them, not as they were written: a floor the
        // window then overrides is a size we asked for and did not get, which reads as a drag below.
        desired.X = Clamp(desired.X, MinWidth * scale, display.X);
        desired.Y = Clamp(desired.Y, MinHeight * scale, display.Y);

        // Appearing only lands while a window is coming up, so on its own a reset would sit there
        // until the panel next reappeared. Insist whenever the size we want is not the size on
        // screen - except mid-drag, where the user's size is not saved yet and insisting would snap
        // the window out from under them on every frame of the gesture.
        var moved = lastSize != Vector2.Zero && Vector2.Distance(desired, lastSize) > 1f;
        var theirs = resizing || ImGui.IsMouseDown(ImGuiMouseButton.Left);

        // Size is the one value on this path Dalamud scales for us, so it is handed over divided.
        // Everything else here - the addon's height, the measured width, a size read back off the
        // window - is already in screen pixels, and appliedSize has to stay in those to be
        // comparable with what the window actually ends up.
        Size = desired / scale;
        appliedSize = desired;

        if (moved && !theirs)
        {
            SizeCondition = ImGuiCond.Always;

            // Bounded, so a size that never settles exactly cannot wedge drag capture off for good.
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

        // Unlike a loot roll, a vendor is a decision you can come back to, so this says how much to
        // trust itself and shows the list anyway rather than refusing.
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

        // The same filters the duty window uses, so each means one thing across the plugin.
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

        // Only the list scrolls, so the buttons and the stale-data warning stay reachable.
        using var list = ImRaii.Child("vendorList", Vector2.Zero, false);
        if (!list.Success)
            return;

        foreach (var group in groups)
            DrawGroup(group, options.GroupBySlot, stale);
    }

    /// <summary>
    /// Keeps a value between two bounds, with the floor winning if the ceiling is below it.
    /// </summary>
    /// <remarks>
    /// The screen can be narrower than the minimum width on a small enough window, and
    /// <see cref="Math.Clamp(float, float, float)"/> throws rather than picking one.
    /// </remarks>
    private static float Clamp(float value, float min, float max) =>
        Math.Max(min, Math.Min(value, max));

    /// <summary>
    /// Stores a size the user dragged to, so it is not thrown away the next time a shop opens.
    /// </summary>
    /// <remarks>
    /// Saved on release, not per frame: a drag changes the size on every one of them.
    ///
    /// Telling our resizes from theirs needs the latch, not just a comparison. On the frame a resize
    /// is requested the window has not moved yet, so a comparison reads as a drag - and since this
    /// runs before the toolbar, a stray size written here is read back by the same frame's toolbar.
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
    /// They write the shared settings rather than vendor-only copies: standing at a vendor is when
    /// you find out you wanted "everything" instead of "my job". Each button shows the state it is
    /// in, not the state it would move to, so it reads as an indicator you can also press.
    /// </remarks>
    private bool DrawToolbar()
    {
        var configuration = plugin.Configuration;
        var changed = false;

        var showOwned = configuration.ShowOwnedItems;
        if (UiParts.ToolButton(
                "##vendorOwned",
                showOwned ? FontAwesomeIcon.Eye : FontAwesomeIcon.EyeSlash,
                showOwned
                    ? "Showing pieces you already have. Click to list only what you are missing.\n" + SharedNote
                    : "Hiding pieces you already have. Click to list them too.\n" + SharedNote))
        {
            configuration.ShowOwnedItems = !showOwned;
            changed = true;
        }

        ImGui.SameLine();

        var jobOnly = configuration.OnlyCurrentJobEquippable;
        if (UiParts.ToolButton(
                "##vendorJob",
                jobOnly ? FontAwesomeIcon.User : FontAwesomeIcon.Users,
                jobOnly
                    ? "Showing only what your current job can wear. Click to show every job.\n" + SharedNote
                    : "Showing gear for every job. Click to show only your current job.\n" + SharedNote))
        {
            configuration.OnlyCurrentJobEquippable = !jobOnly;
            changed = true;
        }

        ImGui.SameLine();

        var hideWeapons = configuration.HideWeapons;
        if (UiParts.ToolButton(
                "##vendorWeapons",
                hideWeapons ? FontAwesomeIcon.Ban : FontAwesomeIcon.Khanda,
                hideWeapons
                    ? "Hiding weapons and off-hands. Click to list them.\n" + SharedNote
                    : "Listing weapons and off-hands. Click to hide them.\n" + SharedNote))
        {
            configuration.HideWeapons = !hideWeapons;
            changed = true;
        }

        // Only once there is something to undo. Without it a drag is a one-way door.
        if (configuration is { VendorPanelWidth: > 0, VendorPanelHeight: > 0 })
        {
            ImGui.SameLine();

            if (UiParts.ToolButton(
                    "##vendorSize",
                    FontAwesomeIcon.ArrowsAltV,
                    "Using a size you set. Click to match the vendor window again."))
            {
                configuration.VendorPanelWidth = 0;
                configuration.VendorPanelHeight = 0;
                changed = true;
            }
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

    private void DrawRow(VendorRow row, bool stale)
    {
        var uncertain = CollectionMarkers.IsUncertain(row.Marker, stale);

        // The glyph carries the state; the name only says whether you need the thing. Hence the
        // untinted name - any colour on it would compete with the glyph for meaning.
        var glyphColour = row.Marker switch
        {
            CollectionMarker.OutfitComplete => Palette.Good,
            CollectionMarker.NotCollected => uncertain ? Palette.Warning : Palette.NotOwned,
            CollectionMarker.Unknown => Palette.Warning,
            _ => Palette.Muted,
        };

        ImGui.AlignTextToFramePadding();

        // Fixed width so the names line up regardless of which glyph each row carries.
        using (Plugin.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
            ImGui.TextColored(glyphColour, Glyph(row.Marker).ToIconString());

        ImGui.SameLine();
        UiParts.ItemIcon(row.IconId, 20);

        if (row.Marker == CollectionMarker.NotCollected)
            ImGui.Text(row.Name);
        else if (row.Marker == CollectionMarker.Unknown)
            ImGui.TextColored(Palette.Warning, row.Name);
        else
            ImGui.TextColored(Palette.Muted, row.Name);

        // Both bind to the last item drawn, so the hover flag has to be taken against the name before
        // the popup goes on the row.
        var hovered = ImGui.IsItemHovered();

        UiParts.ItemContextMenu(plugin, row.ItemId, row.Name);

        if (!hovered)
            return;

        using var tooltip = ImRaii.Tooltip();
        ImGui.Text(row.Name);
        ImGui.TextColored(Palette.Muted, $"iLvl {row.ItemLevel}");
        ImGui.TextColored(glyphColour, CollectionMarkers.Describe(row.Marker));

        if (uncertain && row.Marker == CollectionMarker.NotCollected)
            ImGui.TextColored(Palette.Warning, "Your dresser snapshot predates this - check before buying.");

        ImGui.Spacing();
        ImGui.TextColored(Palette.Muted, "Right-click for options.");
    }

    /// <remarks>
    /// The star marks a finished outfit and nothing else. A star is a reward everywhere else in the
    /// game, so it must never land on the pieces you are missing.
    /// </remarks>
    private static FontAwesomeIcon Glyph(CollectionMarker marker) => marker switch
    {
        CollectionMarker.Dresser => FontAwesomeIcon.Check,
        CollectionMarker.Outfit => FontAwesomeIcon.LayerGroup,
        CollectionMarker.OutfitComplete => FontAwesomeIcon.Star,
        CollectionMarker.Armoire => FontAwesomeIcon.Archive,
        CollectionMarker.Inventory => FontAwesomeIcon.Briefcase,
        CollectionMarker.NotCollected => FontAwesomeIcon.Times,
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
            // Dropped, not counted: these say nothing about your collection, only that you asked
            // not to see them.
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
            if (row.Marker == CollectionMarker.NotCollected)
                group.NotCollected++;
        }

        groups.RemoveAll(group => group.Rows.Count == 0);
        groups.Sort((a, b) => a.Order.CompareTo(b.Order));
    }

    /// <summary>
    /// How wide the panel has to be for the longest item name to fit.
    /// </summary>
    /// <remarks>
    /// Fixing the height means fixing the width too, and a constant would clip exactly the long
    /// names that are hardest to recognise from their first half.
    /// </remarks>
    private void Measure(IReadOnlyList<VendorRow> fresh)
    {
        var widest = 0f;
        foreach (var row in fresh)
            widest = Math.Max(widest, ImGui.CalcTextSize(row.Name).X);

        var style = ImGui.GetStyle();

        // Marker, icon and their spacing, plus room for the scrollbar a long list will have.
        var furniture = (44f * ImGuiHelpers.GlobalScale) +
                        (style.ItemSpacing.X * 2) +
                        (style.WindowPadding.X * 2) +
                        style.ScrollbarSize;

        // Scaled, because everything it is being compared against is: CalcTextSize reports the font
        // at its drawn size, and the floor has to be a floor in the same pixels.
        contentWidth = Math.Max(FallbackWidth * ImGuiHelpers.GlobalScale, widest + furniture);
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
