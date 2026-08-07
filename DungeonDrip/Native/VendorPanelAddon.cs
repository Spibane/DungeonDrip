using System;
using System.Collections.Generic;
using System.Numerics;
using DungeonDrip.Core;
using DungeonDrip.Presentation;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Nodes;

namespace DungeonDrip.Native;

/// <summary>The vendor panel as a game window: gear the shop stocks, and where each piece is held.</summary>
/// <remarks>
/// The first surface drawn natively, and picked because it carries every hard part of a gear list
/// in the fewest lines - a marker whose shape and colour are separate decisions, an item icon, a
/// name, a multi-line tooltip - while riding on an addon that appears and disappears as the player
/// walks up to a counter.
///
/// <b>A standalone addon, never nodes attached inside the shop window.</b> That is a promise the
/// plugin already makes about its panels and keeps here: several popular plugins write to the same
/// game nodes, last writer per frame wins, and a refresh reverts either. It also removes the whole
/// class of crash where the game frees an addon holding the plugin's nodes, because nothing of the
/// plugin's is ever inside anything the game frees.
///
/// It cannot be resized by dragging, unlike its ImGui counterpart, because a game window has no
/// resize handle. It sizes itself to the shop and the player scales it from the header's own
/// right-click menu, which scales the text with it - see the readme's Not implemented section.
/// </remarks>
internal sealed unsafe class VendorPanelAddon : IDisposable
{
    /// <summary>
    /// The panel's width, and its height until a shop is found to match.
    /// </summary>
    /// <remarks>
    /// Fixed, where the ImGui panel measures the longest name and sizes to it. Measuring text is
    /// something ImGui offers and a node tree does not, so the honest first cut is a width that
    /// suits most shop stock; a name too long for it will wrap or clip until this is measured
    /// properly off the font.
    /// </remarks>
    private static readonly Vector2 UnanchoredSize = new(260f, 400f);

    private readonly Plugin plugin;
    private readonly Panel addon;

    /// <summary>Where the marks and the toolbar icons come from. Shared, not this panel's.</summary>
    private readonly GlyphTextures glyphs;

    /// <summary>One menu for every row. See <see cref="NativeContextMenu"/> on why not one each.</summary>
    private readonly NativeContextMenu menu;

    /// <summary>
    /// Shared with the ImGui panels, so a heading folded in one look is folded in the other.
    /// </summary>
    private readonly HeaderState headers;

    /// <summary>The row list this panel was last built from, so an unchanged list is not rebuilt.</summary>
    /// <remarks>
    /// Reference equality against the watcher's cached list, which is the same instance frame to
    /// frame until something actually moves. A native rebuild allocates and frees game nodes, so
    /// unlike an ImGui redraw it is not free and must not happen on a tick.
    /// </remarks>
    private IReadOnlyList<GearRow>? builtFrom;

    /// <summary>Reused between builds, since Regroup refills it in place.</summary>
    private readonly List<SlotGroup> groups = [];

    /// <summary>The filters the current rows were built through.</summary>
    private ViewOptions builtWith;

    public VendorPanelAddon(Plugin plugin, GlyphTextures glyphs)
    {
        this.plugin = plugin;
        this.glyphs = glyphs;
        menu = new NativeContextMenu(plugin);
        headers = new HeaderState(plugin.Configuration);

        addon = new Panel
        {
            Glyphs = glyphs,
            InternalName = "DungeonDripVendor",
            Title = "Dungeon Drip",
            Size = UnanchoredSize,

            // The panel belongs beside its shop, so it is placed every frame and there is no
            // remembered position to restore. Leaving this on would fight the anchoring.
            RememberClosePosition = false,

            // Not the player's to close, exactly as the ImGui panel is not: it belongs to the shop
            // window and comes and goes with it. Both of these default to letting it be closed, and
            // between them they are why the panel used to vanish for good after a few visits -
            // Esc closed the addon, nothing here knew, and it was never reopened.
            DisableClose = true,
            RespectCloseAll = false,
        };

        Plugin.Framework.Update += OnUpdate;
        glyphs.Ready += OnGlyphsReady;
    }

    /// <summary>
    /// Rebuilds once a mark that was missing has been drawn.
    /// </summary>
    /// <remarks>
    /// A glyph is drawn the first time anything asks for it and is kept for the session, so this
    /// fires a handful of times in a session and never again. Forgetting what the panel was built
    /// from is the whole of it - the next tick sees a list it has not built and does the work, on
    /// the framework tick where every other build happens.
    /// </remarks>
    private void OnGlyphsReady() => builtFrom = null;

    /// <summary>
    /// Opens, closes and repositions the panel against whichever shop is up.
    /// </summary>
    /// <remarks>
    /// On the framework tick rather than in a draw callback, which is the real difference from the
    /// ImGui panels: there is no draw callback to be in. A consequence worth watching for is that
    /// the panel can lag its shop by a tick while the shop is being dragged.
    /// </remarks>
    private void OnUpdate(Dalamud.Plugin.Services.IFramework framework)
    {
        var rows = Resolve();

        if (rows == null)
        {
            if (addon.IsOpen)
                addon.Close();

            return;
        }

        // Asked of the addon rather than remembered here. A flag of its own was the bug that made
        // the panel disappear permanently: the addon can close without being told to, and a
        // remembered "still open" then means it is never opened again.
        if (!addon.IsOpen)
            addon.Open();

        // The list only exists once the game has run the addon's setup, which is not guaranteed to
        // have happened by the time Open returns. Leaving builtFrom alone rather than recording a
        // build that did not happen is what makes this a retry next tick instead of a panel that
        // comes up empty and stays that way.
        if (addon.List is not { } list)
            return;

        // The filters have to be part of what decides a rebuild. The watcher hands back the same
        // list instance until the shop's stock changes, so keying on the list alone means a filter
        // toggled in Settings does not reach the panel until the player walks to another vendor.
        var options = PanelPresenter.Options(plugin.Configuration, plugin.Configuration.VendorPanel);

        if (!ReferenceEquals(builtFrom, rows) || options != builtWith)
        {
            builtFrom = rows;
            builtWith = options;
            Rebuild(list, rows);
        }

        Anchor();
    }

    /// <summary>
    /// Re-stacks the list and tells the scroll bar how far it now goes.
    /// </summary>
    /// <remarks>
    /// After filling, and again whenever a heading is folded. Both change how tall the contents
    /// are, and neither the list nor the scroll area notices on its own.
    /// </remarks>
    private void Reflow()
    {
        addon.List?.RecalculateLayout();
        addon.RemeasureScroll();
    }

    /// <summary>
    /// What a filter press has to do beyond writing the setting.
    /// </summary>
    /// <remarks>
    /// The same pair the ImGui panels run, and it has to stay the same pair: these are the settings
    /// the duty list reads, so a filter flipped at a shop that did not rebuild the report would show
    /// as one thing here and another in the main window.
    ///
    /// Nothing rebuilds the panel here. The press only writes; the next tick notices the options
    /// have moved and rebuilds, which keeps every path that builds nodes on the framework tick.
    /// </remarks>
    private void FiltersChanged()
    {
        plugin.Configuration.Save();
        plugin.InvalidateReport();
    }

    /// <summary>The list to draw, or null when the panel should not be up at all.</summary>
    private IReadOnlyList<GearRow>? Resolve()
    {
        if (!plugin.Configuration.VendorPanel.Enabled)
            return null;

        var rows = plugin.Shop.Resolve();

        // A shop with nothing wearable gets no panel. An empty panel at the potion merchant is
        // noise, and the same rule the ImGui panel applies.
        return rows is { Count: > 0 } ? rows : null;
    }

    /// <remarks>
    /// The toolbar is refilled alongside the list because the two go out of date together: what
    /// rebuilds the list is a filter moving, and a filter moving is exactly what changes the glyph
    /// and the tooltip on the button that flipped it.
    ///
    /// The reset-size button the plugin overlays carry is not here and is not missing. There is no
    /// resize handle on a game window, so there is never a dragged size to undo.
    /// </remarks>
    private void Rebuild(VerticalListNode list, IReadOnlyList<GearRow> rows)
    {
        var stale = plugin.Ownership.IsDresserStale;

        addon.Toolbar?.Fill(PanelPresenter.Filters(plugin.Configuration, hasOwnedRows: true, FiltersChanged));

        // Through the same filters the ImGui panel and the duty list use. Rendering the resolved
        // list straight was the first cut and was simply wrong: every filter in Settings appeared
        // to do nothing here, because nothing here was reading them.
        var options = PanelPresenter.Options(plugin.Configuration, plugin.Configuration.VendorPanel);
        PanelGrouping.Regroup(rows, options, groups);

        var built = PanelPresenter.List(groups, stale, PanelText.VendorUncertain, CollapsePrefix);

        NativeRenderer.Fill(menu, glyphs, headers, list, built, addon.ScrollBarWidth, Reflow);

        Reflow();
    }

    /// <summary>
    /// Prefix on the remembered heading state, matching the ImGui vendor panel's.
    /// </summary>
    /// <remarks>
    /// The same string that panel passes, and it has to stay so: it is what makes a slot folded in
    /// one look folded in the other, and what keeps these headings from colliding with the duty
    /// list's, which share the store.
    /// </remarks>
    private const string CollapsePrefix = "vendor:";

    /// <remarks>
    /// The same arithmetic the ImGui panels use, through the shared
    /// <see cref="AddonAnchor"/> - the placement is a fact about the screen and does not change
    /// with the backend. What does change is where the numbers come from: there is no
    /// <c>ImGui.GetIO</c> here, so the screen size is read off the game's own device.
    /// </remarks>
    private void Anchor()
    {
        var name = plugin.Shop.ActiveAddonName;
        if (name == null)
            return;

        var unit = Plugin.GameGui.GetAddonByName<AtkUnitBase>(name);
        if (unit == null)
            return;

        var device = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Device.Instance();
        var display = new Vector2(device->Width, device->Height);

        // Matching the shop's height keeps a long list on screen and lines the two windows up,
        // which is the whole of the sizing story now that dragging one is not on offer.
        var size = new Vector2(addon.Size.X, unit->GetScaledHeight(true));
        addon.SetWindowSize(size.X, size.Y);

        // After the resize, because the toolbar and the list are placed against the size the window
        // now is. Sizing the addon without this leaves the content laid out for the size it was.
        addon.Relayout();

        addon.SetWindowPosition(AddonAnchor.Beside(
            new Vector2(unit->X, unit->Y), unit->GetScaledWidth(true),
            plugin.Configuration.VendorPanel.Side, size, display));
    }

    public void Dispose()
    {
        Plugin.Framework.Update -= OnUpdate;
        glyphs.Ready -= OnGlyphsReady;

        if (addon.IsOpen)
            addon.Close();

        addon.Dispose();
        menu.Dispose();
    }

    /// <summary>The addon itself, and the list of rows inside it.</summary>
    /// <remarks>
    /// The list is built in <see cref="OnSetup"/> and not in a constructor, because there is
    /// nothing to attach it to until then: the root node is allocated by Open, and a node attached
    /// before that attaches to nothing and the panel comes up empty.
    ///
    /// Closing deallocates, so this runs again on every reopen and the list is a different node
    /// each time. That is why the panel forgets what it built from when it closes - the rows it
    /// remembers building no longer exist.
    /// </remarks>
    private sealed class Panel : NativeAddon
    {
        /// <summary>
        /// How much of the content area the toolbar and its rule take off the top of the list.
        /// </summary>
        /// <remarks>
        /// The buttons, the gap under them and the rule. Taken off the scroll area's height as well
        /// as added to its top: a scroll area told to start lower without being told to end at the
        /// same place runs off the bottom of the window, and nothing clips it back.
        /// </remarks>
        private const float ToolbarBandHeight = NativeToolbar.ButtonSize + 10f;

        private ScrollingNode<VerticalListNode>? scroller;
        private HorizontalListNode? buttons;
        private HorizontalLineNode? rule;

        /// <summary>The window size the content was last laid out against.</summary>
        private Vector2 laidOutFor;

        /// <summary>Set before the addon is opened, since there is nothing to attach to until then.</summary>
        public required GlyphTextures Glyphs { private get; init; }

        /// <summary>The filter buttons, or null while the addon is not allocated.</summary>
        public NativeToolbar? Toolbar { get; private set; }

        /// <summary>The list to put rows in, or null while the addon is not allocated.</summary>
        public VerticalListNode? List => scroller?.ContentNode;

        /// <summary>Tells the scroll area its contents changed size. Call after filling the list.</summary>
        public void RemeasureScroll() => scroller?.RecalculateSizes();

        /// <summary>How much of the list's right edge the scroll bar covers.</summary>
        public float ScrollBarWidth => scroller?.ScrollBarNode.Width ?? 0f;

        protected override unsafe void OnSetup(AtkUnitBase* addon, Span<AtkValue> values)
        {
            buttons = new HorizontalListNode { IsVisible = true };

            AddNode(buttons);
            Toolbar = new NativeToolbar(Glyphs, buttons);

            // The same rule the ImGui panels draw under their toolbar, and it earns its line here
            // for the same reason: without it the topmost row reads as another button.
            rule = new HorizontalLineNode { IsVisible = true };
            AddNode(rule);

            scroller = new ScrollingNode<VerticalListNode> { IsVisible = true };

            // FitContents is what tells the scroll area how far there is to scroll, since a list
            // that never reports growing gives it nothing to scroll.
            scroller.ContentNode.FitContents = true;

            // Not FitWidth. That stretches every child to the list's full width, which is the width
            // the scroll bar is drawn over - so a heading came out underneath it. The rows survived
            // it only because their columns are sized individually. Widths are set per node
            // instead, against a width that already has the bar taken off.
            scroller.ContentNode.FitWidth = false;

            AddNode(scroller);

            Relayout();
        }

        /// <summary>
        /// Puts the toolbar, its rule and the list where the window's current size puts them.
        /// </summary>
        /// <remarks>
        /// Needed after every resize and not only after setup, which the first cut got wrong.
        /// Setup runs while the addon is still whatever size it was left at, and the panel is only
        /// matched to the shop a tick later - so a reload with a shop already open laid the content
        /// out against the wrong height and left a band of empty window under the list. Walking away
        /// and back appeared to fix it because by then the remembered size happened to be right.
        ///
        /// A no-op unless the size actually moved. It runs on every framework tick, and re-measuring
        /// the scroll area each time would fight whatever the player has scrolled to.
        /// </remarks>
        public void Relayout()
        {
            if (buttons == null || rule == null || scroller == null || laidOutFor == Size)
                return;

            laidOutFor = Size;

            buttons.Position = ContentStartPosition;
            buttons.Size = new Vector2(ContentSize.X, NativeToolbar.ButtonSize);

            rule.Position = ContentStartPosition + new Vector2(0f, NativeToolbar.ButtonSize + 4f);
            rule.Size = new Vector2(ContentSize.X, 4f);

            scroller.Position = ContentStartPosition + new Vector2(0f, ToolbarBandHeight);
            scroller.Size = ContentSize - new Vector2(0f, ToolbarBandHeight);
            scroller.RecalculateSizes();
        }


        /// <remarks>
        /// Closing deallocates the whole tree, so the references have to go with it. Keeping one
        /// would leave the next visit writing rows into a node the game has already freed, which is
        /// the quiet version of the same bug as the open flag.
        ///
        /// The toolbar is told rather than only dropped, because a glyph texture may still be being
        /// rendered for a button that has just stopped existing.
        /// </remarks>
        protected override unsafe void OnFinalize(AtkUnitBase* addon)
        {
            Toolbar?.Invalidate();
            Toolbar = null;
            scroller = null;
            buttons = null;
            rule = null;

            // So the next setup lays out rather than recognising the size it happens to reopen at.
            laidOutFor = Vector2.Zero;
        }
    }
}
