using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Player;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using DungeonDrip.Core;
using DungeonDrip.Data;
using DungeonDrip.Game;
using DungeonDrip.Windows;

namespace DungeonDrip;

/// <summary>
/// The plugin's entry point: builds everything once, owns it, and drives it from the framework tick.
/// </summary>
/// <remarks>
/// Also the composition root. Nothing here constructs itself on demand, because the order matters -
/// the legacy migration has to run before anything reads a settings file or a cache, and the loot
/// dataset has to exist before the catalogue, the drop index and the report builder derived from it
/// can. Keeping that order in one constructor is what makes it checkable.
///
/// Everything derived from the loot dataset or from the ownership snapshot is rebuilt here rather
/// than by whoever draws it, in <see cref="OnFrameworkUpdate"/>. Both change rarely and are read by
/// every surface, so rebuilding on a revision bump costs one comparison a frame and guarantees the
/// windows cannot disagree about what is collected.
/// </remarks>
public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static IContextMenu ContextMenu { get; private set; } = null!;
    [PluginService] internal static ISigScanner SigScanner { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider GameInterop { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/dungeondrip";

    /// <summary>Tried in order; whichever are free get registered. See <see cref="CommandRegistration"/>.</summary>
    private static readonly string[] CommandAliases = ["/drip", "/ddrip"];

    public Configuration Configuration { get; }
    public LootDataService LootData { get; }
    public OwnershipTracker Ownership { get; }
    public LearnedLootStore LearnedLoot { get; }
    public WikiLootSource Wiki { get; }

    /// <summary>Rebuilt whenever the loot dataset changes; null until the first dataset arrives.</summary>
    public DutyCatalog? Duties { get; private set; }

    /// <summary>The loot tables read backwards, for "where does this drop?".</summary>
    public DropSources? Drops { get; private set; }

    /// <summary>
    /// Every storable piece by name, for looking one up by typing it.
    /// </summary>
    /// <remarks>
    /// Not nullable and not rebuilt, unlike its neighbours above. It reads only the Item sheet, so it
    /// is ready in the constructor and stays correct for the life of the load - which is what lets a
    /// piece be named before the loot download has landed.
    /// </remarks>
    public GearNameIndex GearNames { get; }

    /// <summary>
    /// Where a piece comes from other than a duty, built on first ask.
    /// </summary>
    /// <remarks>
    /// Lazy because the build sweeps every recipe, shop, quest and achievement row in the game -
    /// around 170ms, far past a frame. Doing it at load would be a visible hitch during login for a
    /// question that may never be asked; doing it here makes the one stall coincide with the player
    /// asking for it. Null while <see cref="Configuration.ShowAcquisitionSources"/> is off, so the
    /// setting costs nothing rather than merely hiding the result.
    /// </remarks>
    public Core.Sources.ItemSources? ItemSources =>
        Configuration.ShowAcquisitionSources
            ? itemSources ??= Core.Sources.ItemSources.Build(Storage)
            : null;

    /// <summary>
    /// How a piece can be obtained, as the surfaces that describe one should report it.
    /// </summary>
    /// <remarks>
    /// The one way anything draws acquisition routes, so the sell-back setting cannot be honoured by three
    /// surfaces and forgotten by the fourth. Going through <see cref="ItemSources"/> directly is what a new
    /// surface would do by default, and it would be subtly wrong.
    ///
    /// <b>Returns null when nothing has been consulted</b> - the setting that builds the index is off -
    /// which callers must keep distinct from an empty list meaning "looked, and found nothing". Saying
    /// "source unknown" about a question never asked is the mistake this distinction exists to prevent.
    ///
    /// A plain buy-back is dropped when the setting asks for it; an event piece keeps its line either way.
    /// The two are separated because hiding "Event re-purchase - 47 gil" would leave the lookup answering
    /// "source unknown" about a Pumpkin Head, which the plugin plainly does know the answer to. Filtered
    /// here rather than in the index because the index is built once per load and the setting is not.
    /// </remarks>
    public IReadOnlyList<Core.Sources.AcquisitionSource>? SourcesFor(uint itemId)
    {
        var sources = ItemSources;
        if (sources == null)
            return null;

        var routes = sources.For(itemId);
        if (!Configuration.ExcludeSellBackVendors)
            return routes;

        var kept = new List<Core.Sources.AcquisitionSource>(routes.Count);
        foreach (var route in routes)
        {
            if (route.Repurchase && !route.EventOnly)
                continue;

            kept.Add(route);
        }

        return kept;
    }

    /// <summary>Outfit-set membership, needed anywhere ownership is judged.</summary>
    public OutfitCatalog Outfits { get; }

    /// <summary>Feeds pieces and whole outfits to the game's Fitting Room.</summary>
    public TryOnService TryOn { get; }

    /// <summary>Which store can hold a given piece.</summary>
    public StorageEligibility Storage { get; }

    /// <summary>Shared so every gear list agrees on what the current job can wear.</summary>
    public JobFilter JobFilter { get; } = new();

    /// <summary>Shared for the same reason, for what this character can wear at all.</summary>
    public EquipLockFilter EquipLocks { get; } = new();

    /// <summary>Chat commands that were actually claimed at load.</summary>
    public CommandRegistration Commands { get; }

    /// <summary>The vendor currently in front of the player, and what it is selling.</summary>
    public ShopWatcher Shop { get; }

    /// <summary>Whether the tooltip hook found its function, for Settings to be honest about it.</summary>
    public bool TooltipLineAvailable => tooltipLine.Available;

    /// <summary>One resolved-row cache, shared by every panel.</summary>
    public GearRowFactory Rows { get; }

    /// <summary>Carried gear the Glamour Dresser has not got.</summary>
    public DresserAddWatcher Dresser { get; }

    /// <summary>The same question asked of the Armoire, whose own screen will not answer it.</summary>
    public ArmoireAddWatcher Armoire { get; }

    /// <summary>The gear the market board is currently browsing.</summary>
    public MarketBoardWatcher Market { get; }

    private readonly WindowSystem windowSystem = new("DungeonDrip");
    private readonly ContentFinderIndex contentFinder;
    private readonly JobRoleIndex jobRoles;
    private readonly LootObserver lootObserver;
    private readonly GameContextMenu gameContextMenu;
    private readonly ItemTooltipLine tooltipLine;
    private readonly HttpFetcher http = new();

    private readonly MissingItemsWindow mainWindow;
    private readonly ConfigWindow configWindow;

    private Core.Sources.ItemSources? itemSources;
    private DutyReportBuilder? reportBuilder;
    private RouletteAdviceBuilder? adviceBuilder;
    private IReadOnlyList<RouletteAdvice>? advice;
    private uint currentTerritory;
    private uint? pinnedTerritory;
    private DutyReport? report;
    private bool reportDirty = true;
    private int seenLootRevision = -1;
    private int seenOwnershipRevision = -1;
    private uint seenClassJob;
    private Sex? seenSex;
    private uint seenRace;
    private uint? autoOpenForTerritory;
    private uint wikiRequestedFor;

    public Plugin()
    {
        // Before anything reads settings or caches, since both live under the plugin's name.
        LegacyConfigMigration.Run(PluginInterface);

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        // Before anything reads a panel's settings, so nothing sees the pre-fold defaults.
        if (Configuration.MigrateIfNeeded())
            Configuration.Save();

        var configDirectory = PluginInterface.GetPluginConfigDirectory();

        Outfits = OutfitCatalog.Build();
        TryOn = new TryOnService(Outfits, Configuration);
        contentFinder = ContentFinderIndex.Build();
        jobRoles = JobRoleIndex.Build();
        Storage = StorageEligibility.Build();

        // After the storage filter it depends on, and before anything can be looked up. Not tied to
        // the loot dataset like the catalogues below, because the Item sheet does not move.
        GearNames = GearNameIndex.BuildAll(Storage);
        Ownership = new OwnershipTracker(configDirectory, Configuration);

        LearnedLoot = new LearnedLootStore(configDirectory);
        lootObserver = new LootObserver(Configuration, LearnedLoot, contentFinder, Storage);
        Rows = new GearRowFactory(this);
        Shop = new ShopWatcher(this);
        Dresser = new DresserAddWatcher(this);
        Armoire = new ArmoireAddWatcher(this);
        Market = new MarketBoardWatcher(this);
        gameContextMenu = new GameContextMenu(this);
        tooltipLine = new ItemTooltipLine(this, SigScanner, GameInterop);
        Wiki = new WikiLootSource(configDirectory, Configuration, http);

        // Reads the on-disk copy immediately and starts an update check in the background, so a
        // returning user has data before the first frame and a first-time user gets it shortly after.
        LootData = new LootDataService(configDirectory, LearnedLoot, Wiki, contentFinder, Storage, http);

        mainWindow = new MissingItemsWindow(this);
        configWindow = new ConfigWindow(this);
        windowSystem.AddWindow(mainWindow);
        windowSystem.AddWindow(configWindow);

        // Always open; each decides for itself whether the addon it rides on is up.
        windowSystem.AddWindow(new LootCompanionWindow(this) { IsOpen = true });
        windowSystem.AddWindow(new VendorPanelWindow(this) { IsOpen = true });
        windowSystem.AddWindow(new DresserPanelWindow(this) { IsOpen = true });
        windowSystem.AddWindow(new ArmoirePanelWindow(this) { IsOpen = true });
        windowSystem.AddWindow(new MarketBoardPanelWindow(this) { IsOpen = true });

        currentTerritory = ClientState.TerritoryType;

        Commands = new CommandRegistration(CommandName, CommandAliases, OnCommand);


        ClientState.TerritoryChanged += OnTerritoryChanged;
        Framework.Update += OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        Framework.Update -= OnFrameworkUpdate;
        ClientState.TerritoryChanged -= OnTerritoryChanged;

        foreach (var itemId in referenceLinks.Keys)
            ChatGui.RemoveChatLinkHandler(ReferenceLinkBase + itemId);

        windowSystem.RemoveAllWindows();
        lootObserver.Dispose();
        gameContextMenu.Dispose();
        tooltipLine.Dispose();
        Shop.Dispose();
        Dresser.Dispose();
        Armoire.Dispose();
        Market.Dispose();
        LootData.Dispose();
        Wiki.Dispose();
        http.Dispose();

        Commands.Dispose();
    }

    public DutyReport? Report => report;

    /// <summary>
    /// Which job to queue each roulette as. Built on first ask rather than alongside the report,
    /// because it sweeps every duty in the game and is only wanted while outside one.
    /// </summary>
    public IReadOnlyList<RouletteAdvice> Roulettes =>
        advice ??= adviceBuilder?.Build(Ownership.Current, Configuration) ?? [];

    public uint SelectedTerritory => pinnedTerritory ?? currentTerritory;

    /// <summary>Where the player actually is, as opposed to whichever duty is being looked at.</summary>
    public uint CurrentTerritory => currentTerritory;

    public bool IsPinned => pinnedTerritory.HasValue;

    /// <summary>Which of the main window's two views is showing.</summary>
    public MainWindowMode Mode => Configuration.MainWindowMode;

    public void SetMode(MainWindowMode mode)
    {
        if (Configuration.MainWindowMode == mode)
            return;

        Configuration.MainWindowMode = mode;
        Configuration.Save();
    }

    /// <remarks>
    /// Forces the duty view, because every route into here - the command, the picker, the drop
    /// submenu - is someone asking about a specific duty, and landing them on the collection view
    /// instead would ignore what they just asked for. Doing it here rather than at each call site
    /// is what makes that true of routes added later too.
    /// </remarks>
    public void PinTerritory(uint territoryId)
    {
        pinnedTerritory = territoryId;
        SetMode(MainWindowMode.Duty);
        InvalidateReport();
    }

    public void Unpin()
    {
        pinnedTerritory = null;
        InvalidateReport();
    }

    /// <summary>Pins a duty and brings the window up on it, for anything acted on from elsewhere.</summary>
    public void ShowDuty(uint territoryId)
    {
        PinTerritory(territoryId);
        mainWindow.IsOpen = true;
    }

    /// <summary>Opens the duty picker with a search already typed into it.</summary>
    public void ShowDutyPicker(string filter) => mainWindow.OpenPicker(filter);

    public void InvalidateReport() => reportDirty = true;

    public void ToggleConfigUi() => configWindow.Toggle();

    public void ToggleMainUi() => mainWindow.Toggle();

    /// <summary>
    /// Follows the character between zones: stales the report and decides whether the window should
    /// open or close on its own.
    /// </summary>
    private void OnTerritoryChanged(uint territoryId)
    {
        var leftSupportedDuty = contentFinder.IsSupportedDuty(currentTerritory) &&
                                !contentFinder.IsSupportedDuty(territoryId);

        currentTerritory = territoryId;
        InvalidateReport();

        // A zone change closes the fitting room, so anything still queued for it has nowhere to go.
        TryOn.Clear();

        // A pinned duty means the user is deliberately looking something up, so leave them to it.
        if (leftSupportedDuty && Configuration.CloseWhenLeavingDuty && !pinnedTerritory.HasValue)
            mainWindow.IsOpen = false;

        // Tied to the territory rather than a bare flag so that a duty entered while the dataset is
        // still downloading still pops the window once the data lands - and nothing else does.
        autoOpenForTerritory = Configuration.AutoOpenOnDutyEnter && !pinnedTerritory.HasValue &&
                               contentFinder.IsSupportedDuty(territoryId)
            ? territoryId
            : null;
    }

    /// <summary>
    /// The per-frame spine: pumps the background services, then rebuilds the report if anything it
    /// is derived from has moved.
    /// </summary>
    /// <remarks>
    /// Rebuilding is gated on revision counters and cheap comparisons rather than done every tick,
    /// because a report sweeps a duty's whole loot list. The counters cover the two sources that
    /// change on their own - the dataset and the collection - and the compares cover the character
    /// facts a filter reads, which no revision would notice moving.
    /// </remarks>
    private void OnFrameworkUpdate(IFramework framework)
    {
        Wiki.Update();
        LootData.Update();
        Ownership.Update();
        TryOn.Tick();
        RequestWikiDataIfNeeded();

        if (LootData.Revision != seenLootRevision && LootData.Data != null)
        {
            seenLootRevision = LootData.Revision;
            Duties = DutyCatalog.Build(LootData.Data, contentFinder);
            Drops = DropSources.Build(LootData.Data, Duties);
            reportBuilder = new DutyReportBuilder(
                LootData.Data, Duties, Outfits, jobRoles, Storage, JobFilter, EquipLocks);
            adviceBuilder = new RouletteAdviceBuilder(
                LootData.Data, contentFinder, Outfits, jobRoles, Storage, EquipLocks);
            reportDirty = true;
        }

        if (Ownership.Revision != seenOwnershipRevision)
        {
            seenOwnershipRevision = Ownership.Revision;
            reportDirty = true;
        }

        // Only the job filter cares, but a stale list after switching jobs looks like a bug.
        if (Configuration.OnlyCurrentJobEquippable && PlayerState.IsLoaded &&
            PlayerState.ClassJob.RowId != seenClassJob)
        {
            seenClassJob = PlayerState.ClassJob.RowId;
            reportDirty = true;
        }

        // The same for the gender and race filters. These move once in a blue moon - a Fantasia, or
        // a log in as somebody else - but the list is wrong until it is noticed, and noticing costs
        // two compares.
        if ((Configuration.OnlyCurrentGenderEquippable || Configuration.OnlyCurrentRaceEquippable) &&
            PlayerState.IsLoaded &&
            (PlayerState.Sex != seenSex || PlayerState.Race.RowId != seenRace))
        {
            seenSex = PlayerState.Sex;
            seenRace = PlayerState.Race.RowId;
            reportDirty = true;
        }

        if (!reportDirty)
            return;

        reportDirty = false;
        report = reportBuilder?.Build(SelectedTerritory, Ownership.Current, Configuration);

        // Everything that stales the report stales the advice too - it is the same ownership
        // snapshot read a different way. Dropped rather than rebuilt, so a pinned duty does
        // not pay for a sweep the window will not draw.
        advice = null;

        if (report == null || autoOpenForTerritory != SelectedTerritory)
            return;

        autoOpenForTerritory = null;
        if (report.MissingCount == 0 && Configuration.HideWhenNothingMissing)
            return;

        // Zoning into a dungeon has to land on the duty, not on wherever the window was left. A
        // window that pops itself open to show dresser pressure as the gate drops is noise.
        SetMode(MainWindowMode.Duty);
        mainWindow.IsOpen = true;
    }

    /// <summary>
    /// Asks the wiki about the duty in view. Driven by the selection rather than by the report,
    /// because the duties that need it most are exactly the ones with no report at all.
    /// </summary>
    private void RequestWikiDataIfNeeded()
    {
        var selected = SelectedTerritory;
        if (selected == 0 || selected == wikiRequestedFor)
            return;

        if (!contentFinder.IsSupportedDuty(selected) || !contentFinder.TryGet(selected, out var condition))
            return;

        wikiRequestedFor = selected;
        Wiki.RequestIfStale(selected, condition.Name.ExtractText());
    }

    /// <summary>Forces a fresh wiki lookup for the duty in view, ignoring the cache.</summary>
    public void RefetchWikiForSelection()
    {
        var selected = SelectedTerritory;
        if (selected != 0 && contentFinder.TryGet(selected, out var condition))
            Wiki.RequestIfStale(selected, condition.Name.ExtractText(), force: true);
    }

    /// <summary>
    /// Dispatches a chat command: the fixed sub-commands first, then a duty name, then a gear name.
    /// </summary>
    /// <remarks>
    /// The order is the whole design. Duty names keep absolute priority so nothing that worked
    /// before behaves differently, and a query matching no duty falls through to gear rather than
    /// erroring - which is what lets one command answer both questions without a keyword.
    /// </remarks>
    private void OnCommand(string command, string arguments)
    {
        var args = arguments.Trim();

        switch (args.ToLowerInvariant())
        {
            case "":
                mainWindow.Toggle();
                return;

            case "config":
            case "settings":
                configWindow.Toggle();
                return;

            case "refresh":
                Ownership.RequestRefresh();
                ChatGui.Print("Dungeon Drip will re-read your collection on the next tick.");
                return;

            // Turns "the panel does not work at this vendor" into a report that names the addon.
            case "shop":
            case "vendor":
                ChatGui.Print(Shop.Describe());
                return;

            case "update":
                LootData.CheckForUpdates(force: true);
                RefetchWikiForSelection();
                ChatGui.Print("Dungeon Drip is re-downloading the dungeon loot data.");
                return;
        }

        // Duty search is a substring match, so a piece whose name appears inside a duty's would
        // never be reachable otherwise. This is the way to say "I meant the gear".
        if (args.StartsWith("item ", StringComparison.OrdinalIgnoreCase))
        {
            LookUpGear(args[5..].Trim());
            return;
        }

        if (Duties == null)
        {
            ChatGui.PrintError($"Dungeon Drip: loot data is not loaded yet ({LootData.StatusMessage})");
            return;
        }

        var matches = Duties.Search(args).ToList();
        switch (matches.Count)
        {
            // Falls through to gear rather than erroring. Duty names keep absolute priority, so
            // nothing that worked before behaves differently.
            case 0:
                LookUpGear(args);
                return;

            case 1:
                PinTerritory(matches[0].TerritoryId);
                mainWindow.IsOpen = true;
                return;

            default:
                var exact = matches.FirstOrDefault(
                    m => string.Equals(m.Name, args, StringComparison.OrdinalIgnoreCase));

                if (exact != null)
                {
                    PinTerritory(exact.TerritoryId);
                    mainWindow.IsOpen = true;
                    return;
                }

                mainWindow.OpenPicker(args);
                return;
        }
    }

    /// <summary>
    /// Answers "do I own this, and where does it come from" for a piece named by hand.
    /// </summary>
    /// <remarks>
    /// The answer goes to chat rather than the window. The window is territory-shaped all the way
    /// through - its title comes from the duty, it pins one, and it opens itself on zoning into
    /// one - so an item view would be fighting all three. Chat also gets the item link for free,
    /// which makes the answer hoverable and linkable in a way a window row is not.
    /// </remarks>
    private void LookUpGear(string query)
    {
        if (query.Length == 0)
        {
            ChatGui.PrintError("Dungeon Drip: name a duty or a piece of gear.");
            return;
        }

        // No loot-data gate any more. The name index reads only the Item sheet, so a piece can be
        // named and answered about before the download has landed - only the drop line below waits.
        // An exact name beats any number of partial ones, matching how the duty search resolves the
        // same tie a few lines up.
        if (!GearNames.TryResolve(query, out var itemId))
        {
            var (matches, total) = GearNames.Search(query, MaxGearMatches + 1);
            switch (matches.Count)
            {
                case 0:
                    ChatGui.PrintError(
                        $"Dungeon Drip: nothing matching \"{query}\". Duty names and any piece of " +
                        "gear that can be kept as a glamour are what can be looked up.");
                    return;

                case 1:
                    itemId = matches[0].ItemId;
                    break;

                default:
                    DescribeAmbiguity(query, matches, total);
                    return;
            }
        }

        DescribeGear(itemId);
    }

    /// <summary>
    /// Lists the pieces a query could have meant, as item links so one can be hovered rather than
    /// retyped. Above <see cref="MaxGearMatches"/> it asks for a longer query instead of filling chat.
    /// </summary>
    private void DescribeAmbiguity(
        string query, IReadOnlyList<(uint ItemId, string Name)> matches, int total)
    {
        if (matches.Count > MaxGearMatches)
        {
            // The real total, not "more than 8" - knowing it is 12 rather than 2,770 is what decides
            // between typing three more letters and giving up on the search.
            ChatGui.Print(
                $"Dungeon Drip: {total} pieces match \"{query}\" - try more of the name.");
            return;
        }

        var line = new SeStringBuilder().AddText($"Dungeon Drip: {matches.Count} pieces match \"{query}\" - ");
        for (var i = 0; i < matches.Count; i++)
        {
            if (i > 0)
                line.AddText("  ");

            line.AddItemLink(matches[i].ItemId, false);
        }

        ChatGui.Print(line.Build());
    }

    /// <summary>
    /// Prints one piece's answer: the link, whether it is collected, and every way to obtain it.
    /// </summary>
    /// <remarks>
    /// Drops first, then the other routes, because a duty is the one route this plugin can then take
    /// the player to. The two halves come from different data with opposite failure modes, so their
    /// empty cases are worded separately and the combined "nothing knows anything" case is worded
    /// once, at the end - see <see cref="DescribeNoSource"/>.
    /// </remarks>
    private void DescribeGear(uint itemId)
    {
        ChatGui.Print(new SeStringBuilder()
            .AddText("Dungeon Drip: ")
            .AddItemLink(itemId, false)
            .Build());

        var source = MissingItems.Resolve(
            itemId, Ownership.Current, Outfits.SetsContaining(itemId),
            Configuration.OutfitOwnership, Configuration.Scope);

        // Reported through the same resolver every list uses, so it obeys the storage scope and
        // outfit mode rather than quietly answering a different question from the windows.
        ChatGui.Print(Ownership.HasDresserData
            ? $"   {MissingItems.Describe(source)}."
            : "   No dresser data yet - open a Glamour Dresser once so this can be answered.");

        var drops = Drops?.For(itemId) ?? [];
        if (drops.Count > 0)
        {
            var named = drops.Take(MaxNamedDuties)
                .Select(entry => entry.Level > 0 ? $"{entry.DutyName} (Lv. {entry.Level})" : entry.DutyName);

            var rest = drops.Count - MaxNamedDuties;
            var tail = rest > 0 ? $" and {rest} more" : string.Empty;

            ChatGui.Print($"   Drops in: {string.Join(", ", named)}{tail}.");
        }

        // Read once: the accessor builds the index on first touch, and asking twice would also let the
        // setting change between the two reads. Null means nothing looked, which is not the same as
        // nothing found - see SourcesFor.
        var routes = SourcesFor(itemId);
        var acquisitions = routes ?? [];

        // No cap. One line per kind holds this to three lines in practice and six at the structural
        // worst, so there is nothing left to truncate - see ItemSources.Accumulator.Finish.
        foreach (var acquisition in acquisitions)
            ChatGui.Print($"   {acquisition.Describe()}.");

        // Only claim nothing is known where something actually looked. With the setting off the index
        // was never built, so saying the source is unknown would be reporting a question not asked.
        if (drops.Count == 0 && acquisitions.Count == 0 && routes != null)
            DescribeNoSource(itemId);

        PrintReferenceLink(itemId);
    }

    /// <summary>
    /// Says that nothing was found, briefly, and without overclaiming.
    /// </summary>
    /// <remarks>
    /// "Source unknown", never "cannot be obtained". The loot data is thin for new content by design,
    /// and the sheets - exact about recipes and shops - know nothing of the Mog Station, seasonal
    /// events, PvP series, deep dungeons, treasure maps or relic steps.
    ///
    /// One line, and it used to be three: a sentence naming every dataset consulted, then a second
    /// about the market board. That was a paragraph explaining a failure, where the useful part is the
    /// single word that narrows the search - untradable rules the market board out, marketable rules it
    /// in, and neither needs a clause.
    /// </remarks>
    private void DescribeNoSource(uint itemId)
    {
        var qualifier = string.Empty;
        if (DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>().TryGetRow(itemId, out var item))
        {
            qualifier = item.IsUntradable
                ? " - untradable"
                : item.ItemSearchCategory.RowId != 0 ? " - try the market board" : string.Empty;
        }

        ChatGui.Print($"   Source unknown{qualifier}.");
    }

    /// <summary>
    /// Appends a clickable link to the configured reference site.
    /// </summary>
    /// <remarks>
    /// The label is the site's bare name - see <see cref="Core.Sources.ItemLink.NameOf"/> on why it
    /// carries no verb.
    ///
    /// <para><b>The command id is the item id, offset.</b> Dalamud hands a handler nothing but its own
    /// command id, so the handler has to recover the item from that alone. Encoding it means no state
    /// to go stale and every line correct forever, however many lookups follow.</para>
    ///
    /// <para>A fixed pool of ids cycled round was tried first and was wrong: the ninth lookup reused
    /// the first slot, so clicking the oldest visible line opened whatever had overwritten it. That is
    /// the failure the pool was meant to prevent, merely postponed by eight.</para>
    /// </remarks>
    private void PrintReferenceLink(uint itemId)
    {
        var site = Configuration.LookupSite;

        // Registered on first sight of a piece and left registered. One entry per piece looked up in a
        // session is a few dozen at most, and unregistering would break lines still on screen. The
        // payload has to be the one the registration handed back - it cannot be constructed.
        if (!referenceLinks.TryGetValue(itemId, out var payload))
        {
            payload = ChatGui.AddChatLinkHandler(ReferenceLinkBase + itemId, OnReferenceLinkClicked);
            referenceLinks[itemId] = payload;
        }

        ChatGui.Print(new SeStringBuilder()
            .AddText("   ")
            .Add(payload)
            .AddUiForeground(LinkColour)
            .AddText($"[{Core.Sources.ItemLink.NameOf(site)}]")
            .AddUiForegroundOff()
            .Add(RawPayload.LinkTerminator)
            .Build());
    }

    /// <remarks>
    /// Reads the site out of the configuration now rather than remembering the one in force when the
    /// line was printed, so changing the setting re-points every link already on screen. That is the
    /// more useful of the two behaviours and falls out of holding no state at all.
    /// </remarks>
    private void OnReferenceLinkClicked(uint commandId, SeString _)
    {
        if (commandId < ReferenceLinkBase)
            return;

        var itemId = commandId - ReferenceLinkBase;
        var name = GearNames.TryGetName(itemId, out var resolved) ? resolved : string.Empty;

        Dalamud.Utility.Util.OpenLink(Core.Sources.ItemLink.For(Configuration.LookupSite, itemId, name));
    }

    /// <summary>Partial matches to list before asking for a longer query.</summary>
    private const int MaxGearMatches = 8;

    /// <summary>Duties to name in the chat answer before collapsing the rest into a count.</summary>
    private const int MaxNamedDuties = 3;

    /// <summary>
    /// Added to an item id to make a chat-link command id.
    /// </summary>
    /// <remarks>
    /// Only has to sit above every other command id this plugin registers - which is none - and leave
    /// room above itself for the whole Item sheet.
    /// </remarks>
    private const uint ReferenceLinkBase = 1_000_000;

    /// <summary>
    /// The game's own colour for a clickable link, so this reads as one rather than as coloured text.
    /// </summary>
    /// <remarks>
    /// A raw index into the game's UIColor sheet, as the tooltip marker's colours are - there is no
    /// named enum for these, and a colour from the plugin's own palette would make the one element in
    /// chat that is clickable look least like it.
    /// </remarks>
    private const ushort LinkColour = 34;

    /// <summary>
    /// Link payloads by item id, registered once each and kept for the life of the load.
    /// </summary>
    /// <remarks>
    /// Holds payloads, never URLs. The URL is rebuilt from the item id on every click, which is what
    /// makes an old chat line still correct - storing it is what the discarded pool of cycled ids got
    /// wrong.
    /// </remarks>
    private readonly Dictionary<uint, DalamudLinkPayload> referenceLinks = [];
}
