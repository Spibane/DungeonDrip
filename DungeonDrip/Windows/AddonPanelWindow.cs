using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using DungeonDrip.Core;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace DungeonDrip.Windows;

/// <summary>
/// A panel that rides alongside a game window, listing gear and where each piece is already held.
/// </summary>
/// <remarks>
/// A panel rather than markers on the game's own rows. These lists are long and virtualised - the
/// game recycles a handful of row widgets while scrolling - so an inline glyph has to stay pinned to
/// a moving target, and a marker that slips onto the wrong item is worse than no feature at all. A
/// panel cannot misalign, because it never claims to point at a row.
///
/// What is here is the part that has nothing to do with any particular game window: finding the
/// addon, sizing to it, telling a resize the panel asked for from one the user dragged, remembering the
/// latter, the shared filter buttons, the collapsible headings, and the marker rows. That resize
/// latch is subtle enough that it has been got wrong before, and writing it out a third time by
/// hand is how it would be got wrong again.
///
/// The loot companion deliberately does not sit on this. It is auto-resizing and must stay so - a
/// roll is a thirty-second decision and a window that changes size mid-roll is hostile - it has no
/// toolbar and no grouping by design, and it refuses to draw at all without a dresser snapshot
/// where everything here shows the list with a warning instead. That is a different philosophy, not
/// an accident.
/// </remarks>
public abstract unsafe class AddonPanelWindow : Window
{
    /// <summary>Used until the list has been measured, and as a floor afterwards.</summary>
    private const float FallbackWidth = 260f;

    private const float MinWidth = 180f;
    private const float MinHeight = 120f;

    protected const string SharedNote = "Also applies to the duty list.";

    protected readonly Plugin Plugin;

    private readonly string collapsePrefix;
    private readonly string toolbarId;

    /// <summary>Resolved in DrawConditions and reused for the rest of the frame.</summary>
    private IReadOnlyList<GearRow>? rows;

    /// <summary>Regrouping only happens when the list or the view options actually change.</summary>
    private readonly List<SlotGroup> groups = [];
    private IReadOnlyList<GearRow>? groupedFrom;
    private ViewOptions groupedWith;
    private int hiddenByFilter;

    /// <summary>Width the longest name in the current list needs, measured when it changes.</summary>
    private float contentWidth = FallbackWidth;
    private IReadOnlyList<GearRow>? measuredFrom;

    /// <summary>The size PreDraw wants, so a size the panel chose is not mistaken for a drag.</summary>
    private Vector2 appliedSize;

    /// <summary>The size the window actually had last frame.</summary>
    private Vector2 lastSize;

    private bool resizing;

    /// <summary>Frames left for a requested resize to land, during which drags are not read.</summary>
    private int applying;

    protected AddonPanelWindow(Plugin plugin, string title, string collapsePrefix, string toolbarId)
        : base(title, BaseFlags)
    {
        Plugin = plugin;
        this.collapsePrefix = collapsePrefix;
        this.toolbarId = toolbarId;

        RespectCloseHotkey = false;
        ShowCloseButton = false;
        DisableWindowSounds = true;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(MinWidth, MinHeight),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    // Collapsible and resizable because it cannot be dragged: folding or shrinking it is the only
    // way to get it out of the way short of switching the feature off.
    private static ImGuiWindowFlags BaseFlags =>
        ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav;

    /// <summary>Where this panel's enabled flag, side and dragged size live.</summary>
    protected abstract PanelSettings Settings { get; }

    /// <summary>The addon to sit beside, or null when there is none open.</summary>
    protected abstract string? AnchorAddonName { get; }

    /// <summary>
    /// The list to draw, or null when it could not be read.
    /// </summary>
    /// <remarks>
    /// Null and empty must stay distinguishable at the source: "this could not be read" and "there
    /// is nothing here" both end up drawing no panel, but only one of them is worth a log line, and
    /// conflating them is how an unreadable window quietly starts looking like an empty one.
    /// </remarks>
    protected abstract IReadOnlyList<GearRow>? ResolveRows();

    /// <summary>Drawn above the toolbar, for anything the panel wants to say about itself.</summary>
    protected virtual void DrawHeader() { }

    /// <summary>Extra buttons after the shared filters. Returns whether a setting moved.</summary>
    protected virtual bool DrawExtraToolbarButtons() => false;

    /// <summary>
    /// Whether the show-owned filter means anything here.
    /// </summary>
    /// <remarks>
    /// False on a panel whose rows are all one state by construction, where the button would sit
    /// there doing nothing on either setting.
    /// </remarks>
    protected virtual bool HasOwnedRows => true;

    /// <summary>What to add to a row's tooltip when the snapshot cannot be trusted about it.</summary>
    protected virtual string? UncertainNote => null;

    protected virtual string EmptyMessage => "Nothing new here.";

    protected virtual string FilteredEmptyMessage => "Nothing left after your filters.";

    /// <summary>Whether an empty list is a good outcome rather than a neutral one.</summary>
    protected virtual bool EmptyIsGood => false;

    /// <summary>
    /// Resolves the list and decides whether the panel appears at all, measuring it while it is here.
    /// </summary>
    /// <remarks>
    /// The measurement has to happen here rather than in Draw, because PreDraw consumes the width and
    /// runs first - measuring later would size each window's panel to the previous one's longest name.
    /// </remarks>
    public sealed override bool DrawConditions()
    {
        rows = ResolveRows();

        // A window showing nothing wearable gets no panel at all. An empty panel at the potion
        // merchant is noise.
        if (rows is not { Count: > 0 })
            return false;

        // Here rather than in Draw because PreDraw consumes it and runs first; measuring later would
        // size each window's panel to the previous one's longest name.
        if (!ReferenceEquals(measuredFrom, rows))
        {
            measuredFrom = rows;
            Measure(rows);
        }

        return true;
    }

    /// <summary>
    /// Sizes the panel and parks it beside the addon, insisting only when the size on screen is not
    /// the one wanted.
    /// </summary>
    /// <remarks>
    /// The subtle half is telling the panel's own resizes from the user's. Asking for a size takes a
    /// few frames to land, during which the window is the wrong size and a naive comparison reads
    /// that as a drag - hence the latch that <see cref="RememberUserResize"/> waits on. Mid-drag,
    /// nothing is insisted on at all, because insisting every frame would snap the window out from
    /// under the gesture.
    /// </remarks>
    public sealed override void PreDraw()
    {
        var name = AnchorAddonName;
        if (name == null)
            return;

        var unit = Plugin.GameGui.GetAddonByName<AtkUnitBase>(name);
        if (unit == null)
            return;

        var display = ImGui.GetIO().DisplaySize;
        var scale = ImGuiHelpers.GlobalScale;

        // Matching the addon's height keeps a long list on screen, and lines the two windows up.
        // A size the user dragged to wins.
        var desired = Settings is { Width: > 0, Height: > 0 }
            ? new Vector2(Settings.Width, Settings.Height)
            : new Vector2(contentWidth, unit->GetScaledHeight(true));

        // Against the constraints as ImGui will enforce them, not as they were written: a floor the
        // window then overrides is a size asked for and not got, which reads as a drag below.
        desired.X = Clamp(desired.X, MinWidth * scale, display.X);
        desired.Y = Clamp(desired.Y, MinHeight * scale, display.Y);

        // Appearing only lands while a window is coming up, so on its own a reset would sit there
        // until the panel next reappeared. Insist whenever the wanted size is not the size on
        // screen - except mid-drag, where the user's size is not saved yet and insisting would snap
        // the window out from under them on every frame of the gesture.
        var moved = lastSize != Vector2.Zero && Vector2.Distance(desired, lastSize) > 1f;
        var theirs = resizing || ImGui.IsMouseDown(ImGuiMouseButton.Left);

        // Size is the one value on this path Dalamud scales itself, so it is handed over divided.
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

        Position = AddonAnchor.Beside(unit, Settings.Side, desired);
        PositionCondition = ImGuiCond.Always;
    }

    /// <summary>
    /// The panel body: the subclass's header, the shared toolbar, how much to trust the snapshot,
    /// and the grouped rows.
    /// </summary>
    /// <remarks>
    /// Sealed, and the extension points are the small virtuals above it. Every panel here shows the
    /// same thing in the same order, and the parts that genuinely differ - what to say when the list
    /// is empty, which extra buttons there are, what a stale snapshot means at this window - are
    /// small enough to name individually rather than let a subclass rewrite the body.
    /// </remarks>
    public sealed override void Draw()
    {
        RememberUserResize();

        if (rows == null)
            return;

        DrawHeader();

        if (DrawToolbar())
        {
            Plugin.Configuration.Save();

            // The filters are shared with the duty list, so flipping one here has to rebuild that.
            Plugin.InvalidateReport();
        }

        var ownership = Plugin.Ownership;
        var stale = ownership.IsDresserStale;

        // Unlike a loot roll, these are decisions that can be come back to, so this says how much to
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
            Plugin.Configuration.ShowOwnedItems,
            Plugin.Configuration.OnlyCurrentJobEquippable,
            Plugin.Configuration.OnlyCurrentGenderEquippable,
            Plugin.Configuration.OnlyCurrentRaceEquippable,
            Plugin.Configuration.HideWeapons,
            Settings.GroupBySlot);

        Regroup(rows, options);

        if (groups.Count == 0)
        {
            if (hiddenByFilter > 0)
                ImGui.TextColored(Palette.Muted, FilteredEmptyMessage);
            else
                ImGui.TextColored(EmptyIsGood ? Palette.Good : Palette.Muted, EmptyMessage);

            return;
        }

        // Only the list scrolls, so the buttons and the stale-data warning stay reachable.
        using var list = ImRaii.Child("panelList", Vector2.Zero, false);
        if (!list.Success)
            return;

        foreach (var group in groups)
            DrawGroup(group, options.GroupBySlot, stale);
    }

    /// <summary>
    /// One row: the marker glyph, the icon, the name, its menu and its tooltip.
    /// </summary>
    /// <remarks>
    /// Virtual because a panel whose rows are all the same marker by construction gets nothing from
    /// the glyph and would rather spend that column on something it does know.
    /// </remarks>
    protected virtual void DrawRow(GearRow row, bool stale)
    {
        var uncertain = CollectionMarkers.IsUncertain(row.Marker, stale);

        // The glyph carries the state; the name only says whether the thing is needed. Hence the
        // untinted name - any colour on it would compete with the glyph for meaning.
        var glyphColour = row.Marker switch
        {
            CollectionMarker.OutfitComplete => Palette.Good,
            CollectionMarker.NotCollected => uncertain ? Palette.Warning : Palette.NotOwned,

            // Warning rather than the missing red: the piece is in the box, the rule is what is
            // unsatisfied, and the hover says by how much.
            CollectionMarker.OutfitPartial => Palette.Warning,
            CollectionMarker.Unknown => Palette.Warning,
            _ => Palette.Muted,
        };

        ImGui.AlignTextToFramePadding();

        // Fixed width so the names line up regardless of which glyph each row carries.
        using (Plugin.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
            ImGui.TextColored(glyphColour, Glyph(row.Marker).ToIconString());

        ImGui.SameLine();
        UiParts.ItemIcon(row.IconId, 20);

        if (CollectionMarkers.IsMissing(row.Marker))
            ImGui.Text(row.Name);
        else if (row.Marker == CollectionMarker.Unknown)
            ImGui.TextColored(Palette.Warning, row.Name);
        else
            ImGui.TextColored(Palette.Muted, row.Name);

        // Both bind to the last item drawn, so the hover flag has to be taken against the name
        // before the menu goes on this row.
        var hovered = ImGui.IsItemHovered();

        UiParts.ItemContextMenu(Plugin, row.ItemId, row.Name);

        if (!hovered)
            return;

        using var tooltip = ImRaii.Tooltip();
        ImGui.Text(row.Name);
        ImGui.Text(CollectionMarkers.Describe(row.Marker, row.OutfitsStored, row.OutfitsTotal));

        if (uncertain && UncertainNote != null)
            ImGui.TextColored(Palette.Warning, UncertainNote);

        ImGui.Spacing();
        ImGui.TextColored(Palette.Muted, "Right-click for options.");
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
    /// Stores a size the user dragged to, so it is not thrown away the next time the panel opens.
    /// </summary>
    /// <remarks>
    /// Saved on release, not per frame: a drag changes the size on every one of them.
    ///
    /// Telling the panel's own resizes from the user's needs the latch, not just a comparison. On the frame a resize
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

        Settings.Width = size.X;
        Settings.Height = size.Y;
        Plugin.Configuration.Save();
    }

    /// <summary>
    /// The list filters, as buttons rather than a trip to Settings.
    /// </summary>
    /// <remarks>
    /// They write the shared settings rather than per-panel copies: standing at a vendor is where the
    /// wrong filter gets noticed. Each button shows the state it is in, not the state it would move to,
    /// so it reads as an indicator that can also be pressed.
    /// </remarks>
    private bool DrawToolbar()
    {
        var configuration = Plugin.Configuration;
        var changed = false;

        if (HasOwnedRows)
        {
            var showOwned = configuration.ShowOwnedItems;
            if (UiParts.ToolButton(
                    $"##{toolbarId}Owned",
                    showOwned ? FontAwesomeIcon.Eye : FontAwesomeIcon.EyeSlash,
                    showOwned
                        ? "Showing pieces you already have. Click to list only what you are missing.\n" + SharedNote
                        : "Hiding pieces you already have. Click to list them too.\n" + SharedNote))
            {
                configuration.ShowOwnedItems = !showOwned;
                changed = true;
            }

            ImGui.SameLine();
        }

        var jobOnly = configuration.OnlyCurrentJobEquippable;
        if (UiParts.ToolButton(
                $"##{toolbarId}Job",
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
                $"##{toolbarId}Weapons",
                hideWeapons ? FontAwesomeIcon.Ban : FontAwesomeIcon.Khanda,
                hideWeapons
                    ? "Hiding weapons and off-hands. Click to list them.\n" + SharedNote
                    : "Listing weapons and off-hands. Click to hide them.\n" + SharedNote))
        {
            configuration.HideWeapons = !hideWeapons;
            changed = true;
        }

        // Only once there is something to undo. Without it a drag is a one-way door.
        if (Settings is { Width: > 0, Height: > 0 })
        {
            ImGui.SameLine();

            if (UiParts.ToolButton(
                    $"##{toolbarId}Size",
                    FontAwesomeIcon.ArrowsAltV,
                    "Using a size you set. Click to match the game window again."))
            {
                Settings.Width = 0;
                Settings.Height = 0;
                changed = true;
            }
        }

        changed |= DrawExtraToolbarButtons();

        ImGui.Separator();
        return changed;
    }

    private void Regroup(IReadOnlyList<GearRow> fresh, ViewOptions options)
    {
        if (ReferenceEquals(groupedFrom, fresh) && groupedWith == options)
            return;

        groupedFrom = fresh;
        groupedWith = options;
        hiddenByFilter = PanelGrouping.Regroup(fresh, options, groups);
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

        var key = collapsePrefix + group.Label;
        var collapsed = Plugin.Configuration.CollapsedGroups.Contains(key);
        ImGui.SetNextItemOpen(!collapsed, ImGuiCond.Appearing);

        var open = ImGui.CollapsingHeader($"{label}###{key}");
        if (open == collapsed)
        {
            if (open)
                Plugin.Configuration.CollapsedGroups.Remove(key);
            else
                Plugin.Configuration.CollapsedGroups.Add(key);

            Plugin.Configuration.Save();
        }

        if (!open)
            return;

        foreach (var row in group.Rows)
            DrawRow(row, stale);

        ImGui.Spacing();
    }

    /// <summary>
    /// How wide the panel has to be for the longest item name to fit.
    /// </summary>
    /// <remarks>
    /// Fixing the height means fixing the width too, and a constant would clip exactly the long
    /// names that are hardest to recognise from their first half.
    /// </remarks>
    private void Measure(IReadOnlyList<GearRow> fresh)
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

    /// <remarks>
    /// The star marks a finished outfit and nothing else. A star is a reward everywhere else in the
    /// game, so it must never land on the pieces still missing.
    /// </remarks>
    protected static FontAwesomeIcon Glyph(CollectionMarker marker) => marker switch
    {
        CollectionMarker.Dresser => FontAwesomeIcon.Check,
        CollectionMarker.Outfit => FontAwesomeIcon.LayerGroup,
        CollectionMarker.OutfitComplete => FontAwesomeIcon.Star,

        // Half-filled, not a cross and not a star: the outfit-shaped states share the layered look
        // and this one is the one that is partway there.
        CollectionMarker.OutfitPartial => FontAwesomeIcon.Adjust,
        CollectionMarker.Armoire => FontAwesomeIcon.Archive,
        CollectionMarker.Inventory => FontAwesomeIcon.Briefcase,

        // Not a second case. A retainer is somewhere to be travelled to, and the figure says so.
        CollectionMarker.Retainer => FontAwesomeIcon.UserTag,

        // The dressed figure, because this one is not in any bag: the retainer has it on.
        CollectionMarker.RetainerEquipped => FontAwesomeIcon.UserTie,
        CollectionMarker.NotCollected => FontAwesomeIcon.Times,
        _ => FontAwesomeIcon.Question,
    };
}
