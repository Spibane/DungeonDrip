using System;
using System.Linq;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using GlamourAssistant.Core;
using GlamourAssistant.Data;
using GlamourAssistant.Game;
using GlamourAssistant.Windows;

namespace GlamourAssistant;

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
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/glamassist";
    private const string ShortCommandName = "/gla";

    public Configuration Configuration { get; }
    public LootDataService LootData { get; }
    public OwnershipTracker Ownership { get; }
    public LearnedLootStore LearnedLoot { get; }
    public WikiLootSource Wiki { get; }

    /// <summary>Rebuilt whenever the loot dataset changes; null until the first dataset arrives.</summary>
    public DutyCatalog? Duties { get; private set; }

    /// <summary>Outfit-set membership, needed anywhere ownership is judged.</summary>
    public OutfitCatalog Outfits => outfits;

    private readonly WindowSystem windowSystem = new("GlamourAssistant");
    private readonly OutfitCatalog outfits;
    private readonly ContentFinderIndex contentFinder;
    private readonly LootObserver lootObserver;
    private readonly MissingItemsWindow mainWindow;
    private readonly ConfigWindow configWindow;
    private readonly LootCompanionWindow lootCompanionWindow;

    private DutyReportBuilder? reportBuilder;
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
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        var configDirectory = PluginInterface.GetPluginConfigDirectory();

        outfits = OutfitCatalog.Build();
        contentFinder = ContentFinderIndex.Build();
        Ownership = new OwnershipTracker(configDirectory, Configuration);

        LearnedLoot = new LearnedLootStore(configDirectory);
        lootObserver = new LootObserver(Configuration, LearnedLoot, contentFinder);
        Wiki = new WikiLootSource(configDirectory, Configuration);

        // Reads the on-disk copy immediately and starts an update check in the background, so a
        // returning user has data before the first frame and a first-time user gets it shortly after.
        LootData = new LootDataService(configDirectory, LearnedLoot, Wiki);

        mainWindow = new MissingItemsWindow(this);
        configWindow = new ConfigWindow(this);
        lootCompanionWindow = new LootCompanionWindow(this) { IsOpen = true };
        windowSystem.AddWindow(mainWindow);
        windowSystem.AddWindow(configWindow);
        windowSystem.AddWindow(lootCompanionWindow);

        currentTerritory = ClientState.TerritoryType;

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Show dungeon gear you are missing. "
                        + "\"/glamassist <duty name>\" looks up any duty, \"config\" opens settings, "
                        + "\"refresh\" re-reads your dresser, \"update\" re-downloads the loot data.",
        });

        CommandManager.AddHandler(ShortCommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Alias for /glamassist.",
        });

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
        mainWindow.Dispose();
        configWindow.Dispose();
        lootCompanionWindow.Dispose();
        lootObserver.Dispose();
        LootData.Dispose();
        Wiki.Dispose();

        CommandManager.RemoveHandler(CommandName);
        CommandManager.RemoveHandler(ShortCommandName);
    }

    public DutyReport? Report => report;

    public uint SelectedTerritory => pinnedTerritory ?? currentTerritory;

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
        currentTerritory = territoryId;
        InvalidateReport();

        // Tied to the territory rather than a bare flag so that a duty entered while the dataset is
        // still downloading still pops the window once the data lands - and nothing else does.
        autoOpenForTerritory = Configuration.AutoOpenOnDutyEnter && !pinnedTerritory.HasValue
            ? territoryId
            : null;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        Wiki.Update();
        LootData.Update();
        Ownership.Update();
        RequestWikiDataIfNeeded();

        if (LootData.Revision != seenLootRevision && LootData.Data != null)
        {
            seenLootRevision = LootData.Revision;
            Duties = DutyCatalog.Build(LootData.Data, contentFinder);
            reportBuilder = new DutyReportBuilder(LootData.Data, Duties, outfits);
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

        if (!contentFinder.TryGet(selected, out var condition))
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
                ChatGui.Print("Glamour Assistant will re-read your collection on the next tick.");
                return;

            case "update":
                LootData.CheckForUpdates(force: true);
                RefetchWikiForSelection();
                ChatGui.Print("Glamour Assistant is re-downloading the dungeon loot data.");
                return;
        }

        if (Duties == null)
        {
            ChatGui.PrintError($"Glamour Assistant: loot data is not loaded yet ({LootData.StatusMessage})");
            return;
        }

        var matches = Duties.Search(args).ToList();
        switch (matches.Count)
        {
            case 0:
                ChatGui.PrintError($"Glamour Assistant: no duty matching \"{args}\".");
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
