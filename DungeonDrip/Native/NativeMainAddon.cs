using System;
using System.Collections.Generic;
using System.Numerics;
using DungeonDrip.Core;
using DungeonDrip.Presentation;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Nodes;

namespace DungeonDrip.Native;

/// <summary>The plugin's main window as a game window: a duty's loot, or the collection.</summary>
/// <remarks>
/// The only surface here the player opens rather than one that appears beside something. So it is
/// the only one with a close button and a remembered position, and the only one whose open state is
/// something the rest of the plugin sets rather than works out.
///
/// <b>Both views, in one window, and that is why this arrived after the collection view was
/// described rather than with the duty view.</b> The mode button is in the toolbar: a window that
/// could draw only one of the two would have a control in it that switched to something it could not
/// show, and the ways around that were all worse than waiting - a window that changes look when a
/// button in it is pressed, or a view that cannot be reached at all in this look.
///
/// What it does not have is the overlay's resize handle, since no game window does. It remembers
/// where it was put and is scaled from its own header.
/// </remarks>
internal sealed unsafe class NativeMainAddon : IDisposable
{
    private static readonly Vector2 InitialSize = new(560f, 640f);

    /// <summary>
    /// How many duties the picker offers before it asks for a narrower search.
    /// </summary>
    /// <remarks>
    /// A cap rather than a scroll area, which was the first cut and brought its own trouble: a scroll
    /// area inside a scroll area, in a window whose contents are rebuilt underneath it. Every duty in
    /// the game matches an empty search, so something has to bound this - and a search that has not
    /// narrowed to a screenful is one that has not been narrowed yet.
    /// </remarks>
    private const int PickerLimit = 12;

    private readonly Plugin plugin;
    private readonly Main addon;
    private readonly NativeContextMenu menu;
    private readonly GlyphTextures glyphs;
    private readonly HeaderState headers;
    private readonly CollectionSnapshot snapshot;

    private string dutyFilter = string.Empty;
    private bool pickerOpen;

    /// <summary>Per-session, as in the overlay: "I expanded a list once" is not a choice to store.</summary>
    private bool showAllSets;

    private readonly HashSet<uint> showAllOffers = [];

    /// <summary>What the window was last built from, so an unchanged window is not rebuilt.</summary>
    /// <remarks>
    /// Every value the contents depend on, compared as one. A missing entry here does not show up as
    /// a crash but as a window that quietly stops keeping up, so the list is deliberately long: what
    /// the report is, which view is on, every setting a control on the window can flip, and the
    /// per-session expansions.
    /// </remarks>
    private object? built;

    private VerticalListNode? builtInto;

    /// <summary>Whether the body needs re-stacking on the next tick, without being rebuilt.</summary>
    /// <remarks>
    /// Folding a heading changes its height and nothing above it, so the answer is to re-stack what
    /// is already there rather than to build it again - a rebuild works and is what this did first,
    /// but every fold then flickered the whole window.
    ///
    /// <b>Next tick rather than now, and that is the part that was missing.</b> Re-stacking inside
    /// the fold itself ran while the heading was still settling its own height, so everything below
    /// it was placed against the height it had a moment ago and overlapped whatever the fold pushed
    /// down. A tick later every height agrees.
    /// </remarks>
    private bool restack;

    public NativeMainAddon(Plugin plugin, GlyphTextures glyphs)
    {
        this.plugin = plugin;
        this.glyphs = glyphs;
        menu = new NativeContextMenu(plugin);
        headers = new HeaderState(plugin.Configuration);
        snapshot = new CollectionSnapshot(plugin);

        addon = new Main
        {
            Glyphs = glyphs,
            OnTabSelected = SelectTab,
            InternalName = "DungeonDripMain",
            Title = "Dungeon Drip",
            Size = InitialSize,
        };

        Plugin.Framework.Update += OnUpdate;
        glyphs.Ready += OnGlyphsReady;
    }

    /// <summary>
    /// Whether the window is up, as the rest of the plugin asks it.
    /// </summary>
    /// <remarks>
    /// Held here rather than asked of the addon, unlike every panel, because this one is the
    /// player's to close: the addon closing is an answer to be read back, not the state itself. The
    /// tick below carries a close back into this so the toolbar button and the command agree with
    /// what is on screen.
    /// </remarks>
    public bool IsOpen { get; set; }

    public void Toggle() => IsOpen = !IsOpen;

    /// <summary>Opens the window with the picker already filtered, for <c>/dungeondrip &lt;name&gt;</c>.</summary>
    public void OpenPicker(string filter)
    {
        dutyFilter = filter;
        pickerOpen = true;
        IsOpen = true;
        Invalidate();
    }

    private void OnUpdate(Dalamud.Plugin.Services.IFramework framework)
    {
        // A close is the player's, so it is read back rather than fought. Nothing else in the plugin
        // would otherwise know the window had gone.
        if (!addon.IsOpen && IsOpen && addon.WasOpened)
            IsOpen = false;

        if (!IsOpen)
        {
            if (addon.IsOpen)
                addon.Close();

            return;
        }

        if (!addon.IsOpen)
            addon.Open();

        if (addon.List is not { } list)
            return;

        if (plugin.Mode == MainWindowMode.Collection)
            snapshot.Refresh();

        addon.SetTitle(Title());

        var signature = Signature();

        if (!ReferenceEquals(builtInto, list) || !Equals(built, signature))
        {
            builtInto = list;
            built = signature;
            Rebuild(list);
            return;
        }

        if (!restack)
            return;

        restack = false;

        list.RecalculateLayout();
        addon.RemeasureScroll();
        addon.RefreshCollision();
    }

    /// <remarks>
    /// The window is named for whatever it is about, as the overlay is. A duty's name is the most
    /// useful thing a title bar can hold here, since the window is otherwise identical whichever
    /// duty it is reporting on.
    /// </remarks>
    private string Title() => plugin.Mode == MainWindowMode.Collection
        ? "Collection"
        : plugin.Report?.Name ?? "Dungeon Drip";

    private object Signature() => (
        plugin.Report,
        plugin.Mode,
        plugin.Configuration.Grouping,
        plugin.Configuration.ShowOwnedItems,
        plugin.Configuration.ReadyToBuyOutfitsOnly,
        plugin.Configuration.ShowRoleSummary,
        plugin.Configuration.CollectionTab,
        plugin.Ownership.Revision,
        plugin.Duties != null,
        pickerOpen,
        dutyFilter,
        showAllSets,
        showAllOffers.Count);

    /// <remarks>
    /// The toolbar is chrome and stays put; everything else goes in the scrolling body, including
    /// the duty picker and the collection's tab strip. Those two are built here rather than
    /// described, because each holds state a list of nodes cannot carry - a field being typed into,
    /// a strip remembering which tab is on.
    /// </remarks>
    private void Rebuild(VerticalListNode list)
    {
        addon.Toolbar?.Fill(DutyPresenter.Toolbar(plugin, pickerOpen, TogglePicker));

        var collection = plugin.Mode == MainWindowMode.Collection;

        // Before the body is filled, because it decides how much room the body has.
        addon.ShowTabs(collection, plugin.Configuration.CollectionTab);

        list.Clear();

        if (collection)
            BuildCollection(list);
        else
            BuildDuty(list);

        list.RecalculateLayout();
        addon.RemeasureScroll();

        // Explicitly, after everything is in place. Attaching a node updates the addon's collision
        // list, but a rebuild attaches and frees dozens of them in one tick, and what a click can
        // reach is decided by that list rather than by what is on screen.
        addon.RefreshCollision();
    }

    private void BuildDuty(VerticalListNode list)
    {
        Append(list, DutyPresenter.FreshnessBanner(plugin.Ownership));

        if (plugin.Duties == null)
        {
            Append(list, DutyPresenter.NoLootData(plugin));
            return;
        }

        if (pickerOpen)
            BuildPicker(list);

        var report = plugin.Report;

        if (report == null)
        {
            Append(list, DutyPresenterRoulettes.Advice(plugin));
            return;
        }

        Append(list, DutyPresenter.Summary(report));
        Append(list, DutyPresenter.RoleFocus(plugin, report));
        Append(list, [new RuleNode()]);
        Append(list, DutyPresenterLists.Items(plugin, report));
    }

    /// <remarks>
    /// A real search field and a real list box, which is what this look has and the overlay does
    /// not - there a filtered list is a text box, a child region and a run of selectables. The
    /// wording of each row still comes from the presenter, so the two cannot describe a duty
    /// differently.
    /// </remarks>
    private void BuildPicker(VerticalListNode list)
    {
        var width = list.Width - addon.ScrollBarWidth;

        var search = new SearchInputNode
        {
            PlaceholderString = "Search duties by name...",
            Size = new Vector2(width, 28f),
            OnInputComplete = typed =>
            {
                dutyFilter = typed.ToString();
                Invalidate();
            },
            IsVisible = true,
        };

        var outline = NativePalette.For(ColourRole.Default).Outline;
        search.PlaceholderTextNode.TextColor = NativePalette.Solid(ColourRole.Muted);
        search.PlaceholderTextNode.TextOutlineColor = outline;

        list.AddNode(search);

        // After it is attached, and not in the initialiser above. The field hands its text to a
        // component, and the component has nowhere to put it until the node belongs to an addon - so
        // text set before that went in and was never painted. It appeared the moment the field was
        // clicked into, which is the first time the component had reason to look at it again.
        search.String = dutyFilter;

        var entries = DutyPickerPresenter.Entries(plugin, dutyFilter);
        var shown = System.Math.Min(entries.Count, PickerLimit);

        for (var index = 0; index < shown; index++)
        {
            var territory = entries[index].TerritoryId;

            list.AddNode(new TextButtonNode
            {
                String = entries[index].Label,
                Size = new Vector2(width, 22f),
                OnClick = () =>
                {
                    plugin.PinTerritory(territory);
                    pickerOpen = false;
                    Invalidate();
                },
                IsVisible = true,
            });
        }

        if (entries.Count > shown)
        {
            Append(list, [
                new Presentation.TextNode(
                    $"{entries.Count - shown} more - type to narrow the search.", ColourRole.Muted),
            ]);
        }

        Append(list, [new RuleNode()]);
    }

    /// <remarks>
    /// The tab strip is a real one here, where the overlay draws ImGui's. Which tab is on is the
    /// same stored setting in both, so switching look does not lose the reader's place.
    /// </remarks>
    private void BuildCollection(VerticalListNode list) =>
        Append(list, plugin.Configuration.CollectionTab switch
        {
            CollectionTab.Buy => ShoppingPresenter.Tab(plugin, snapshot, showAllOffers, ToggleOffers),
            CollectionTab.Dresser => DresserTabPresenter.Tab(plugin, snapshot),
            _ => CollectionPresenter.Sets(plugin, snapshot, showAllSets, ToggleAllSets),
        });

    private void SelectTab(CollectionTab tab)
    {
        if (plugin.Configuration.CollectionTab == tab)
            return;

        plugin.Configuration.CollectionTab = tab;
        plugin.Configuration.Save();
        Invalidate();
    }

    private void ToggleAllSets()
    {
        showAllSets = !showAllSets;
        Invalidate();
    }

    private void ToggleOffers(uint currencyItemId)
    {
        if (!showAllOffers.Remove(currencyItemId))
            showAllOffers.Add(currencyItemId);

        Invalidate();
    }

    private void TogglePicker()
    {
        pickerOpen = !pickerOpen;
        Invalidate();
    }

    /// <summary>
    /// Says the window is out of date, so the next tick rebuilds it.
    /// </summary>
    /// <remarks>
    /// Needed for the things the signature cannot see: a list expanded past its cap, a picker opened,
    /// a duty typed into the search field. Those are this window's own state rather than the
    /// plugin's, and nothing else would notice them moving.
    /// </remarks>
    private void Invalidate() => built = null;

    private void Append(VerticalListNode list, IReadOnlyList<Node> nodes) =>
        NativeRenderer.Append(
            menu, glyphs, headers, list, nodes, addon.ScrollBarWidth, () => restack = true);

    private void OnGlyphsReady() => Invalidate();

    public void Dispose()
    {
        Plugin.Framework.Update -= OnUpdate;
        glyphs.Ready -= OnGlyphsReady;

        if (addon.IsOpen)
            addon.Close();

        addon.Dispose();
        menu.Dispose();
    }

    /// <summary>The addon itself: a toolbar that stays put, and a body that scrolls.</summary>
    private sealed unsafe class Main : NativeAddon
    {
        private const float ChromeSpacing = 4f;

        /// <summary>Space between one thing in the body and the next.</summary>
        private const float BodySpacing = 3f;
        private const float RuleHeight = 4f;
        private const float TabHeight = 28f;

        private VerticalListNode? chrome;
        private HorizontalListNode? buttons;
        private TabBarNode? tabs;
        private ScrollingNode<VerticalListNode>? scroller;

        /// <summary>The window size and chrome height the content was last laid out against.</summary>
        private (Vector2 Size, float Chrome) laidOutFor;

        public NativeToolbar? Toolbar { get; private set; }

        public VerticalListNode? List => scroller?.ContentNode;

        public float ScrollBarWidth => scroller?.ScrollBarNode.Width ?? 0f;

        public void RemeasureScroll() => scroller?.RecalculateSizes();

        /// <summary>Whether the addon has ever been opened, so a close can be told from a start.</summary>
        public bool WasOpened { get; private set; }

        public required GlyphTextures Glyphs { private get; init; }

        /// <summary>What to do when a tab is pressed, since the strip is built before there is one.</summary>
        public required Action<CollectionTab> OnTabSelected { private get; init; }

        /// <summary>
        /// Rebuilds the addon's list of what a click can reach.
        /// </summary>
        /// <remarks>
        /// Attaching a node updates this by itself, which is enough for a window that grows one node
        /// at a time. A rebuild attaches and frees dozens in a tick, and what is reachable is decided
        /// by that list rather than by what is on screen - so it is said once, at the end, rather
        /// than trusted to have kept up.
        /// </remarks>
        public void RefreshCollision()
        {
            if (InternalAddon is not null)
                InternalAddon->UpdateCollisionNodeList(false);
        }

        /// <summary>
        /// Shows or hides the collection's tab strip, and puts it on the tab being shown.
        /// </summary>
        /// <remarks>
        /// The strip selects its first tab when it is built, which is not necessarily the one whose
        /// contents are about to be drawn - the tab is a stored setting and survives a restart. Left
        /// alone, the window came up showing one tab's name and another tab's list.
        /// </remarks>
        public void ShowTabs(bool visible, CollectionTab selected)
        {
            if (tabs == null || chrome == null)
                return;

            tabs.IsVisible = visible;

            if (visible)
                tabs.SelectTab(Label(selected));

            chrome.RecalculateLayout();
            Relayout();
        }

        private static string Label(CollectionTab tab) => tab switch
        {
            CollectionTab.Buy => "Ready to buy",
            CollectionTab.Dresser => "Glamour Dresser",
            _ => "Sets in progress",
        };

        public void SetTitle(string title)
        {
            if (Title.ToString() == title)
                return;

            Title = title;
            WindowNode?.SetTitle(title);
        }

        protected override unsafe void OnSetup(AtkUnitBase* addon, Span<AtkValue> values)
        {
            WasOpened = true;

            // One fitted list, as the panels use, because the strip is there in one view and not the
            // other - so how much room the body has is not a constant.
            chrome = new VerticalListNode
            {
                FitContents = true,
                FitWidth = true,
                ItemSpacing = ChromeSpacing,
                IsVisible = true,
            };

            buttons = new HorizontalListNode { Height = NativeToolbar.ButtonSize, IsVisible = true };
            chrome.AddNode(buttons);
            Toolbar = new NativeToolbar(Glyphs, buttons);

            chrome.AddNode(new HorizontalLineNode { Height = RuleHeight, IsVisible = true });

            tabs = new TabBarNode { Height = TabHeight, IsVisible = false };
            tabs.AddTab("Sets in progress", () => OnTabSelected(CollectionTab.Sets));
            tabs.AddTab("Ready to buy", () => OnTabSelected(CollectionTab.Buy));
            tabs.AddTab("Glamour Dresser", () => OnTabSelected(CollectionTab.Dresser));
            chrome.AddNode(tabs);

            scroller = new ScrollingNode<VerticalListNode> { IsVisible = true };
            scroller.ContentNode.FitContents = true;
            scroller.ContentNode.FitWidth = false;

            // A gap between everything in the body. Without it the headings in a long list sit flush
            // against each other, and the only part of one that answered a click was the last few
            // pixels - which is the shape of two things overlapping rather than of a hit area being
            // wrong. It reads better spaced out regardless.
            scroller.ContentNode.ItemSpacing = BodySpacing;

            // <b>The scroll area is attached before the chrome, and the order is load-bearing.</b> A
            // scroll area clips what it draws but not what a click can reach, so a list scrolled up
            // puts its rows' collision over whatever sits above it - and of two overlapping nodes
            // the later sibling wins. Attached last, the chrome stays reachable however far the
            // list is scrolled; attached first, its buttons went dead the moment one was.
            AddNode(scroller);
            AddNode(chrome);

            Relayout();
        }

        public void Relayout()
        {
            if (chrome == null || scroller == null)
                return;

            if (System.Math.Abs(chrome.Width - ContentSize.X) > 0.5f)
            {
                chrome.Width = ContentSize.X;
                chrome.RecalculateLayout();
            }

            var key = (Size, chrome.Height);
            if (laidOutFor == key)
                return;

            laidOutFor = key;
            chrome.Position = ContentStartPosition;

            var taken = chrome.Height + ChromeSpacing;

            scroller.Position = ContentStartPosition + new Vector2(0f, taken);
            scroller.Size = ContentSize - new Vector2(0f, taken);
            scroller.RecalculateSizes();
        }

        protected override unsafe void OnFinalize(AtkUnitBase* addon)
        {
            Toolbar?.Invalidate();
            Toolbar = null;

            chrome = null;
            buttons = null;
            tabs = null;
            scroller = null;
            laidOutFor = default;
        }
    }
}
