using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DungeonDrip.Core;
using DungeonDrip.Presentation;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Nodes;

namespace DungeonDrip.Native;

/// <summary>A panel that rides alongside a game window, drawn as a game window itself.</summary>
/// <remarks>
/// The counterpart to <c>Windows.AddonPanelWindow</c>, and deliberately the same shape: every panel
/// shows the same things in the same order, and what genuinely differs between them - which addon to
/// sit beside, what to say above the list, what an empty list means there - is small enough to name
/// individually rather than let a subclass rewrite the body.
///
/// <b>A standalone addon, never nodes attached inside the window it sits beside.</b> That is a
/// promise the plugin already makes about its panels and keeps here: several popular plugins write
/// to the same game nodes, last writer per frame wins, and a refresh reverts either. It also removes
/// the whole class of crash where the game frees an addon holding the plugin's nodes, because
/// nothing of the plugin's is ever inside anything the game frees.
///
/// None of these can be resized by dragging, unlike their overlay counterparts, because a game
/// window has no resize handle. Each sizes itself to the window it accompanies and the player scales
/// it from the header's own right-click menu, which scales the text with it.
///
/// The whole of it runs on the framework tick rather than in a draw callback, which is the real
/// difference from the overlays: there is no draw callback to be in. A consequence worth watching
/// for is that a panel can lag the window it follows by a tick while that window is dragged.
/// </remarks>
internal abstract unsafe class NativePanelAddon : IDisposable
{
    /// <summary>
    /// The panel's width, and its height until a window is found to match.
    /// </summary>
    /// <remarks>
    /// Fixed, where the overlays measure the longest name and size to it. Measuring text is
    /// something ImGui offers and a node tree does not, so the honest answer is a width that suits
    /// most lists; a name too long for it is cut with an ellipsis and given in full on hover.
    /// </remarks>
    private static readonly Vector2 UnanchoredSize = new(260f, 400f);

    /// <summary>
    /// How often the snapshot warning is worked out again, in milliseconds.
    /// </summary>
    /// <remarks>
    /// The warning is the one thing on a panel that goes out of date on its own: it says how long
    /// ago the dresser was read, and it appears when that crosses the staleness threshold with
    /// nothing else having moved. An overlay gets this for nothing by redrawing every frame.
    ///
    /// Two seconds because the wording is never finer than a minute, so anything quicker would be
    /// working out the same sentence to throw it away - and rebuilding nodes to say what they
    /// already say is churn a redraw would not have been.
    /// </remarks>
    private const long NoticeRecheckMs = 2000;

    protected readonly Plugin Plugin;

    private readonly Panel addon;

    /// <summary>Where the marks and the toolbar icons come from. Shared, not this panel's.</summary>
    private readonly GlyphTextures glyphs;

    /// <summary>One menu for every row. See <see cref="NativeContextMenu"/> on why not one each.</summary>
    private readonly NativeContextMenu menu;

    /// <summary>Shared with the overlays, so a heading folded in one look is folded in the other.</summary>
    private readonly HeaderState headers;

    /// <summary>The row list this panel was last built from, so an unchanged list is not rebuilt.</summary>
    /// <remarks>
    /// Reference equality against the watcher's cached list, which is the same instance frame to
    /// frame until something actually moves. A native rebuild allocates and frees game nodes, so
    /// unlike an overlay's redraw it is not free and must not happen on a tick.
    /// </remarks>
    private IReadOnlyList<GearRow>? builtFrom;

    /// <summary>The list node those rows were built into.</summary>
    /// <remarks>
    /// <b>Not the same question as what they were built from, and the panel came up blank without
    /// it.</b> Closing an addon deallocates its whole tree and reopening builds a new one, so a
    /// panel that closes and reopens while the gear behind it has not moved has somewhere new to
    /// draw and nothing telling it to. Watching the node rather than a flag says exactly that: the
    /// rows this remembers building no longer exist.
    /// </remarks>
    private VerticalListNode? builtInto;

    /// <summary>Reused between builds, since Regroup refills it in place.</summary>
    private readonly List<SlotGroup> groups = [];

    /// <summary>The filters the current rows were built through.</summary>
    private ViewOptions builtWith;

    /// <summary>What the notice currently says, so it is only rebuilt when it would differ.</summary>
    private string? noticeShown;

    private long noticeCheckedAt;

    protected NativePanelAddon(Plugin plugin, GlyphTextures glyphs, string internalName, string title)
    {
        Plugin = plugin;
        this.glyphs = glyphs;
        menu = new NativeContextMenu(plugin);
        headers = new HeaderState(plugin.Configuration);

        addon = new Panel
        {
            Glyphs = glyphs,
            InternalName = internalName,
            Title = title,
            Size = UnanchoredSize,

            // No close button on the header. These panels are not the player's to close - each
            // belongs to the window it accompanies and comes and goes with it - and DisableClose
            // below does not remove the button, it only changes what pressing it does. Left there,
            // it shut the panel and the next tick reopened it, which reads as a broken button.
            CreateWindowNode = () => new WindowNode { NodeId = 2, ShowCloseButton = false },

            // The panel belongs beside its window, so it is placed every frame and there is no
            // remembered position to restore. Leaving this on would fight the anchoring.
            RememberClosePosition = false,

            // Not the player's to close, exactly as an overlay panel is not: it belongs to the
            // window it accompanies and comes and goes with it. Both of these default to letting it
            // be closed, and between them they are why a panel used to vanish for good after a few
            // visits - Esc closed the addon, nothing here knew, and it was never reopened.
            DisableClose = true,
            RespectCloseAll = false,
        };

        Plugin.Framework.Update += OnUpdate;
        glyphs.Ready += OnGlyphsReady;
    }

    /// <summary>Where this panel's enabled flag and side live.</summary>
    /// <remarks>
    /// The width and height in there are dead in this look and deliberately not read: there is no
    /// resize handle on a game window, so a size the player dragged an overlay to says nothing about
    /// where this one should be.
    /// </remarks>
    protected abstract PanelSettings Settings { get; }

    /// <summary>The addon to sit beside, or null when there is none open.</summary>
    protected abstract string? AnchorAddonName { get; }

    /// <summary>Prefix on the remembered heading state, matching this panel's overlay.</summary>
    /// <remarks>
    /// The same string that panel passes, and it has to stay so: it is what makes a slot folded in
    /// one look folded in the other, and what keeps one panel's headings from colliding with
    /// another's, since they share the store.
    /// </remarks>
    protected abstract string CollapsePrefix { get; }

    /// <summary>The list to draw, or null when it could not be read.</summary>
    protected abstract IReadOnlyList<GearRow>? ResolveRows();

    /// <summary>Shown above the toolbar, for anything the panel wants to say about itself.</summary>
    protected virtual IReadOnlyList<Node> Header() => [];

    /// <summary>Extra buttons after the shared filters.</summary>
    protected virtual IReadOnlyList<ToolButtonModel> ExtraToolButtons() => [];

    /// <summary>Whether the show-owned filter means anything here.</summary>
    protected virtual bool HasOwnedRows => true;

    /// <summary>What to add to a row's tooltip when the snapshot cannot be trusted about it.</summary>
    protected virtual string? UncertainNote => null;

    /// <summary>Whether how old the dresser snapshot is is a caveat on this panel's list.</summary>
    protected virtual bool DependsOnDresserSnapshot => true;

    protected virtual string EmptyMessage => "Nothing new here.";

    protected virtual string FilteredEmptyMessage => "Nothing left after your filters.";

    /// <summary>Whether an empty list is a good outcome rather than a neutral one.</summary>
    protected virtual bool EmptyIsGood => false;

    /// <summary>One row, described. Overridden where the marker column is dead by construction.</summary>
    protected virtual Node Row(GearRow row, bool stale) =>
        PanelPresenter.Row(row, stale, UncertainNote);

    /// <summary>
    /// Opens, closes and repositions the panel against whichever window it follows.
    /// </summary>
    private void OnUpdate(Dalamud.Plugin.Services.IFramework framework)
    {
        var rows = Resolve();

        if (rows == null)
        {
            if (addon.IsOpen)
                addon.Close();

            return;
        }

        // Asked of the addon rather than remembered here. A flag of its own was the bug that made a
        // panel disappear permanently: the addon can close without being told to, and a remembered
        // "still open" then means it is never opened again.
        if (!addon.IsOpen)
            addon.Open();

        // The list only exists once the game has run the addon's setup, which is not guaranteed to
        // have happened by the time Open returns. Leaving builtFrom alone rather than recording a
        // build that did not happen is what makes this a retry next tick instead of a panel that
        // comes up empty and stays that way.
        if (addon.List is not { } list)
            return;

        // The filters have to be part of what decides a rebuild. A watcher hands back the same list
        // instance until what it is watching changes, so keying on the list alone means a filter
        // toggled in Settings does not reach the panel until the player walks to another window.
        var options = PanelPresenter.Options(Plugin.Configuration, Settings);

        if (!ReferenceEquals(builtInto, list) || !ReferenceEquals(builtFrom, rows) ||
            options != builtWith)
        {
            builtInto = list;
            builtFrom = rows;
            builtWith = options;
            Rebuild(list, rows);
        }

        ShowNotice(force: false);
        Anchor();
    }

    /// <summary>The list to draw, or null when the panel should not be up at all.</summary>
    private IReadOnlyList<GearRow>? Resolve()
    {
        if (!Settings.Enabled)
            return null;

        var rows = ResolveRows();

        // A window showing nothing wearable gets no panel. An empty panel at the potion merchant is
        // noise, and it is the same rule the overlays apply.
        return rows is { Count: > 0 } ? rows : null;
    }

    /// <remarks>
    /// The toolbar and the header are refilled alongside the list because they go out of date
    /// together: what rebuilds the list is a filter moving, and a filter moving is exactly what
    /// changes the glyph and the tooltip on the button that flipped it.
    ///
    /// The reset-size button the overlays carry is not here and is not missing. There is no resize
    /// handle on a game window, so there is never a dragged size to undo.
    /// </remarks>
    private void Rebuild(VerticalListNode list, IReadOnlyList<GearRow> rows)
    {
        var stale = DependsOnDresserSnapshot && Plugin.Ownership.IsDresserStale;

        addon.SetHeader(Header());

        var buttons = new List<ToolButtonModel>(
            PanelPresenter.Filters(Plugin.Configuration, HasOwnedRows, FiltersChanged));

        buttons.AddRange(ExtraToolButtons());
        addon.Toolbar?.Fill(buttons);

        // Forced, because a rebuild follows the addon being set up afresh and the nodes the notice
        // was last written into no longer exist - so what it says now is not what is on screen.
        ShowNotice(force: true);

        // Through the same filters the overlays and the duty list use. Rendering the resolved list
        // straight was the first cut and was simply wrong: every filter in Settings appeared to do
        // nothing here, because nothing here was reading them.
        var options = PanelPresenter.Options(Plugin.Configuration, Settings);
        var hidden = PanelGrouping.Regroup(rows, options, groups);

        if (groups.Count == 0)
        {
            NativeRenderer.Notice(
                list,
                PanelPresenter.Empty(hidden > 0, EmptyMessage, FilteredEmptyMessage, EmptyIsGood),
                list.Width - addon.ScrollBarWidth);
        }
        else
        {
            var built = PanelPresenter.List(groups, CollapsePrefix, row => Row(row, stale));
            NativeRenderer.Fill(menu, glyphs, headers, list, built, addon.ScrollBarWidth, Reflow);
        }

        Reflow();
    }

    /// <summary>
    /// Puts how far the dresser snapshot can be trusted above the list, when there is anything to say.
    /// </summary>
    /// <remarks>
    /// Unlike a loot roll, what these panels show is a decision that can be come back to - so a panel
    /// says how much to trust itself and shows the list anyway rather than refusing to draw.
    /// </remarks>
    private void ShowNotice(bool force)
    {
        if (!DependsOnDresserSnapshot)
            return;

        var now = Environment.TickCount64;

        if (!force && now - noticeCheckedAt < NoticeRecheckMs)
            return;

        noticeCheckedAt = now;

        var nodes = PanelPresenter.SnapshotWarning(Plugin.Ownership, Plugin.Ownership.IsDresserStale);
        var says = string.Join('\n', nodes.OfType<Presentation.TextNode>().Select(node => node.Text));

        if (!force && says == noticeShown)
            return;

        noticeShown = says;
        addon.SetNotice(nodes);
    }

    /// <summary>
    /// What a filter press has to do beyond writing the setting.
    /// </summary>
    /// <remarks>
    /// The same pair the overlays run, and it has to stay the same pair: these are the settings the
    /// duty list reads, so a filter flipped at a shop that did not rebuild the report would show as
    /// one thing here and another in the main window.
    ///
    /// Nothing rebuilds the panel here. The press only writes; the next tick notices the options
    /// have moved and rebuilds, which keeps every path that builds nodes on the framework tick.
    /// </remarks>
    protected void FiltersChanged()
    {
        Plugin.Configuration.Save();
        Plugin.InvalidateReport();
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
    /// Re-stacks the list and tells the scroll bar how far it now goes.
    /// </summary>
    /// <remarks>
    /// After filling, and again whenever a heading is folded. Both change how tall the contents are,
    /// and neither the list nor the scroll area notices on its own.
    /// </remarks>
    private void Reflow()
    {
        addon.List?.RecalculateLayout();
        addon.RemeasureScroll();
    }

    /// <remarks>
    /// The same arithmetic the overlays use, through the shared <see cref="AddonAnchor"/> - the
    /// placement is a fact about the screen and does not change with the backend. What does change
    /// is where the numbers come from: there is no <c>ImGui.GetIO</c> here, so the screen size is
    /// read off the game's own device.
    /// </remarks>
    private void Anchor()
    {
        var name = AnchorAddonName;
        if (name == null)
            return;

        var unit = Plugin.GameGui.GetAddonByName<AtkUnitBase>(name);
        if (unit == null)
            return;

        var device = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Device.Instance();
        var display = new Vector2(device->Width, device->Height);

        // Against the window rather than the rectangle the game reports for it - see
        // <see cref="AddonFrame"/> on why those are not always the same thing.
        var frame = AddonFrame.For(name);
        var corner = new Vector2(unit->X + frame.Left, unit->Y + frame.Top);
        var width = unit->GetScaledWidth(true) - frame.Left - frame.Right;

        // Matching the window's height keeps a long list on screen and lines the two up, which is
        // the whole of the sizing story now that dragging one is not on offer.
        var size = new Vector2(
            addon.Size.X, unit->GetScaledHeight(true) - frame.Top - frame.Bottom);

        addon.SetWindowSize(size.X, size.Y);

        // After the resize, because the chrome and the list are placed against the size the window
        // now is. Sizing the addon without this leaves the content laid out for the size it was.
        addon.Relayout();

        addon.SetWindowPosition(
            AddonAnchor.Beside(corner, width, Settings.Side, size, display));
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

    /// <summary>The addon itself: a band of chrome that stays put, and a list that scrolls.</summary>
    /// <remarks>
    /// Everything is built in <see cref="OnSetup"/> and not in a constructor, because there is
    /// nothing to attach to until then: the root node is allocated by Open, and a node attached
    /// before that attaches to nothing and the panel comes up empty.
    ///
    /// Closing deallocates, so this runs again on every reopen and every node is a different one
    /// each time. That is why the panel forgets what it built from when it closes - the rows it
    /// remembers building no longer exist.
    /// </remarks>
    private sealed class Panel : NativeAddon
    {
        /// <summary>Space between the parts of the chrome.</summary>
        private const float ChromeSpacing = 4f;

        /// <summary>How thick the game draws a dividing rule.</summary>
        private const float RuleHeight = 4f;

        private VerticalListNode? chrome;
        private VerticalListNode? header;
        private HorizontalListNode? buttons;
        private VerticalListNode? notice;
        private ScrollingNode<VerticalListNode>? scroller;

        /// <summary>The window size and chrome height the content was last laid out against.</summary>
        private (Vector2 Size, float Chrome) laidOutFor;

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

        /// <remarks>
        /// The chrome is one fitted list rather than three nodes placed by hand. All three of its
        /// parts change height - a header appears only on some panels, a notice only on some visits -
        /// and stacking them in something that measures itself is what keeps the arithmetic below
        /// down to one number.
        /// </remarks>
        protected override unsafe void OnSetup(AtkUnitBase* addon, Span<AtkValue> values)
        {
            chrome = new VerticalListNode
            {
                FitContents = true,

                // Unlike the row list, which sizes its columns itself against a width that has the
                // scroll bar taken off. Nothing in the chrome is under the bar, and the rule and the
                // button row have no width of their own to keep.
                FitWidth = true,
                ItemSpacing = ChromeSpacing,
                IsVisible = true,
            };

            AddNode(chrome);

            header = new VerticalListNode { FitContents = true, IsVisible = false };
            chrome.AddNode(header);

            buttons = new HorizontalListNode { Height = NativeToolbar.ButtonSize, IsVisible = true };

            chrome.AddNode(buttons);
            Toolbar = new NativeToolbar(Glyphs, buttons);

            // The same rule the overlays draw under their toolbar, and it earns its line here for
            // the same reason: without it the topmost row reads as another button.
            chrome.AddNode(new HorizontalLineNode { Height = RuleHeight, IsVisible = true });

            notice = new VerticalListNode { FitContents = true, IsVisible = false };
            chrome.AddNode(notice);

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

        /// <summary>Puts what the panel says about itself above its buttons.</summary>
        public void SetHeader(IReadOnlyList<Node> nodes) => SetBand(header, nodes);

        /// <summary>Puts how far the list can be trusted between the buttons and the list.</summary>
        public void SetNotice(IReadOnlyList<Node> nodes) => SetBand(notice, nodes);

        /// <remarks>
        /// Hidden when it has nothing in it, rather than left at zero height. A list skips invisible
        /// children when it measures, so an empty band left visible would still spend the spacing on
        /// either side of itself and open a gap where there is nothing to separate.
        /// </remarks>
        private void SetBand(VerticalListNode? band, IReadOnlyList<Node> nodes)
        {
            if (band == null || chrome == null)
                return;

            NativeRenderer.Notice(band, nodes, ContentSize.X);
            band.IsVisible = nodes.Count > 0;

            chrome.RecalculateLayout();
            Relayout();
        }

        /// <summary>
        /// Puts the chrome and the list where the window's current size puts them.
        /// </summary>
        /// <remarks>
        /// Needed after every resize and not only after setup, which the first cut got wrong. Setup
        /// runs while the addon is still whatever size it was left at, and a panel is only matched to
        /// the window it follows a tick later - so a reload with that window already open laid the
        /// content out against the wrong height and left a band of empty window under the list.
        ///
        /// A no-op unless something actually moved. It runs on every framework tick, and re-measuring
        /// the scroll area each time would fight whatever the player has scrolled to.
        /// </remarks>
        public void Relayout()
        {
            if (chrome == null || scroller == null)
                return;

            // Width first, and before the height is read: it is what the chrome hands down to the
            // rule and the button row, and a list whose children have no width measures itself as
            // though they were not there.
            if (Math.Abs(chrome.Width - ContentSize.X) > 0.5f)
            {
                chrome.Width = ContentSize.X;
                chrome.RecalculateLayout();
            }

            var key = (Size, chrome.Height);
            if (laidOutFor == key)
                return;

            laidOutFor = key;

            chrome.Position = ContentStartPosition;

            // The band the list gets is what is left after the chrome. Moving the top without taking
            // the same off the height is what runs a scroll area off the bottom of the window, where
            // nothing clips it back.
            var taken = chrome.Height + ChromeSpacing;

            scroller.Position = ContentStartPosition + new Vector2(0f, taken);
            scroller.Size = ContentSize - new Vector2(0f, taken);
            scroller.RecalculateSizes();
        }

        /// <remarks>
        /// Closing deallocates the whole tree, so the references have to go with it. Keeping one
        /// would leave the next visit writing rows into a node the game has already freed, which is
        /// the quiet version of the same bug as the open flag.
        ///
        /// The toolbar is told rather than only dropped, because it holds which button the pointer
        /// was last over and nothing will send a mouse-out for a node that has stopped existing.
        /// </remarks>
        protected override unsafe void OnFinalize(AtkUnitBase* addon)
        {
            Toolbar?.Invalidate();
            Toolbar = null;

            chrome = null;
            header = null;
            buttons = null;
            notice = null;
            scroller = null;

            // So the next setup lays out rather than recognising the size it happens to reopen at.
            laidOutFor = default;
        }
    }
}
