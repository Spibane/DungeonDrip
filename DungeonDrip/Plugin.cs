using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using DungeonDrip.Core;
using DungeonDrip.Data;
using DungeonDrip.Game;
using DungeonDrip.Windows;

namespace DungeonDrip;

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

    /// <summary>Outfit-set membership, needed anywhere ownership is judged.</summary>
    public OutfitCatalog Outfits { get; }

    /// <summary>Feeds pieces and whole outfits to the game's Fitting Room.</summary>
    public TryOnService TryOn { get; }

    /// <summary>Which store can hold a given piece.</summary>
    public StorageEligibility Storage { get; }

    /// <summary>Shared so every gear list agrees on what the current job can wear.</summary>
    public JobFilter JobFilter { get; } = new();

    /// <summary>Chat commands that were actually claimed at load.</summary>
    public CommandRegistration Commands { get; }

    /// <summary>The vendor currently in front of the player, and what it is selling.</summary>
    public ShopWatcher Shop { get; }

    private readonly WindowSystem windowSystem = new("DungeonDrip");
    private readonly ContentFinderIndex contentFinder;
    private readonly JobRoleIndex jobRoles;
    private readonly LootObserver lootObserver;
    private readonly HttpFetcher http = new();

    private readonly MissingItemsWindow mainWindow;
    private readonly ConfigWindow configWindow;

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
    private uint? autoOpenForTerritory;
    private uint wikiRequestedFor;

    public Plugin()
    {
        // Before anything reads settings or caches, since both live under the plugin's name.
        LegacyConfigMigration.Run(PluginInterface);

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        var configDirectory = PluginInterface.GetPluginConfigDirectory();

        Outfits = OutfitCatalog.Build();
        TryOn = new TryOnService(Outfits, Configuration);
        contentFinder = ContentFinderIndex.Build();
        jobRoles = JobRoleIndex.Build();
        Storage = StorageEligibility.Build();
        Ownership = new OwnershipTracker(configDirectory, Configuration);

        LearnedLoot = new LearnedLootStore(configDirectory);
        lootObserver = new LootObserver(Configuration, LearnedLoot, contentFinder, Storage);
        Shop = new ShopWatcher(this);
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

        windowSystem.RemoveAllWindows();
        lootObserver.Dispose();
        Shop.Dispose();
        LootData.Dispose();
        Wiki.Dispose();
        http.Dispose();

        Commands.Dispose();
    }

    public DutyReport? Report => report;

    /// <summary>
    /// Which job to queue each roulette as. Built on first ask rather than alongside the report,
    /// because it sweeps every duty in the game and is only wanted while you are not in one.
    /// </summary>
    public IReadOnlyList<RouletteAdvice> Roulettes =>
        advice ??= adviceBuilder?.Build(Ownership.Current, Configuration) ?? [];

    public uint SelectedTerritory => pinnedTerritory ?? currentTerritory;

    /// <summary>Where the player actually is, as opposed to whichever duty is being looked at.</summary>
    public uint CurrentTerritory => currentTerritory;

    public bool IsPinned => pinnedTerritory.HasValue;

    public void PinTerritory(uint territoryId)
    {
        pinnedTerritory = territoryId;
        InvalidateReport();
    }

    public void Unpin()
    {
        pinnedTerritory = null;
        InvalidateReport();
    }

    public void InvalidateReport() => reportDirty = true;

    public void ToggleConfigUi() => configWindow.Toggle();

    public void ToggleMainUi() => mainWindow.Toggle();

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
            reportBuilder = new DutyReportBuilder(LootData.Data, Duties, Outfits, jobRoles, Storage, JobFilter);
            adviceBuilder = new RouletteAdviceBuilder(LootData.Data, contentFinder, Outfits, jobRoles, Storage);
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

        if (!reportDirty)
            return;

        reportDirty = false;
        report = reportBuilder?.Build(SelectedTerritory, Ownership.Current, Configuration);

        // Everything that stales the report stales the advice too - it is the same ownership
        // snapshot read a different way. Dropped rather than rebuilt, so a duty you pinned does
        // not pay for a sweep the window will not draw.
        advice = null;

        if (report == null || autoOpenForTerritory != SelectedTerritory)
            return;

        autoOpenForTerritory = null;
        if (report.MissingCount > 0 || !Configuration.HideWhenNothingMissing)
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

        if (Duties == null)
        {
            ChatGui.PrintError($"Dungeon Drip: loot data is not loaded yet ({LootData.StatusMessage})");
            return;
        }

        var matches = Duties.Search(args).ToList();
        switch (matches.Count)
        {
            case 0:
                ChatGui.PrintError($"Dungeon Drip: no duty matching \"{args}\".");
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
}
