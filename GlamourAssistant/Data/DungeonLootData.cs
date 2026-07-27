using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Lumina.Excel.Sheets;

namespace GlamourAssistant.Data;

/// <summary>
/// The downloaded dungeon -> item mapping, resolved against the current game data and keyed by
/// territory.
/// </summary>
/// <remarks>
/// Built on the framework thread because it reads Lumina sheets: the dataset stores map ids, which
/// only become territory ids by way of the Map sheet. Doing that translation here rather than at
/// download time keeps a cached dataset valid across game patches.
/// </remarks>
public sealed class DungeonLootData
{
    public const string OverridesFileName = "loot-overrides.json";

    private readonly Dictionary<uint, uint[]> itemsByTerritory;
    private readonly Dictionary<uint, string> fallbackNames;
    private readonly Dictionary<uint, Dictionary<uint, LootProvenance>> provenance;

    /// <summary>When the underlying dataset was downloaded.</summary>
    public DateTime FetchedUtc { get; }

    /// <summary>Duties whose listed drops contained no glamour-able gear at all.</summary>
    public int EmptyAfterFilter { get; }

    public int OverrideCount { get; }

    private DungeonLootData(
        Dictionary<uint, uint[]> itemsByTerritory,
        Dictionary<uint, string> fallbackNames,
        Dictionary<uint, Dictionary<uint, LootProvenance>> provenance,
        DateTime fetchedUtc,
        int emptyAfterFilter,
        int overrideCount)
    {
        this.itemsByTerritory = itemsByTerritory;
        this.fallbackNames = fallbackNames;
        this.provenance = provenance;
        FetchedUtc = fetchedUtc;
        EmptyAfterFilter = emptyAfterFilter;
        OverrideCount = overrideCount;
    }

    /// <summary>Where this piece's entry came from, for showing the user why it is listed.</summary>
    public LootProvenance ProvenanceOf(uint territoryId, uint itemId) =>
        provenance.TryGetValue(territoryId, out var items) && items.TryGetValue(itemId, out var source)
            ? source
            : LootProvenance.Dataset;

    public IReadOnlyCollection<uint> Territories => itemsByTerritory.Keys;

    public int DutyCount => itemsByTerritory.Count;

    public bool TryGetItems(uint territoryId, out uint[] items) =>
        itemsByTerritory.TryGetValue(territoryId, out items!);

    public string? GetFallbackName(uint territoryId) =>
        fallbackNames.TryGetValue(territoryId, out var name) ? name : null;

    public static DungeonLootData Build(
        LootCacheFile cache,
        string configDirectory,
        LearnedLootStore learned,
        WikiLootSource wiki)
    {
        var maps = Plugin.DataManager.GetExcelSheet<Map>();
        var items = Plugin.DataManager.GetExcelSheet<Item>();

        var accumulated = new Dictionary<uint, HashSet<uint>>();
        var names = new Dictionary<uint, string>();
        var empty = 0;

        foreach (var instance in cache.Instances)
        {
            if (!maps.TryGetRow(instance.Map, out var map))
                continue;

            var territoryId = map.TerritoryType.RowId;
            if (territoryId == 0)
                continue;

            var gear = instance.Items.Where(id => IsGlamourableGear(items, id)).ToList();
            if (gear.Count == 0)
            {
                empty++;
                continue;
            }

            if (!accumulated.TryGetValue(territoryId, out var set))
                accumulated[territoryId] = set = [];

            set.UnionWith(gear);
            names.TryAdd(territoryId, instance.Name);
        }

        var provenance = new Dictionary<uint, Dictionary<uint, LootProvenance>>();
        var overrides = ApplyOverrides(configDirectory, accumulated, provenance);

        // Supplementary sources are layered on last. Each records which pieces only exist because
        // of it, so the UI can show provenance - and because they can add a duty the primary
        // dataset omits entirely, they use the same "create the territory if absent" path.
        foreach (var (territoryId, entry) in wiki.All)
            Merge(items, accumulated, provenance, territoryId, entry.Items, LootProvenance.Wiki);

        foreach (var (territoryId, seen) in learned.All)
            Merge(items, accumulated, provenance, territoryId, seen, LootProvenance.Learned);

        var byTerritory = accumulated
            .Where(kv => kv.Value.Count > 0)
            .ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());

        return new DungeonLootData(byTerritory, names, provenance, cache.FetchedUtc, empty, overrides);
    }

    /// <summary>
    /// Adds a supplementary source's items, tagging only the ones it actually contributed. An item
    /// the primary dataset already listed keeps its original provenance.
    /// </summary>
    private static void Merge(
        Lumina.Excel.ExcelSheet<Item> items,
        Dictionary<uint, HashSet<uint>> accumulated,
        Dictionary<uint, Dictionary<uint, LootProvenance>> provenance,
        uint territoryId,
        IEnumerable<uint> itemIds,
        LootProvenance source)
    {
        foreach (var itemId in itemIds)
        {
            if (!IsGlamourableGear(items, itemId))
                continue;

            if (!accumulated.TryGetValue(territoryId, out var set))
                accumulated[territoryId] = set = [];

            if (!set.Add(itemId))
                continue;

            if (!provenance.TryGetValue(territoryId, out var tagged))
                provenance[territoryId] = tagged = [];

            tagged[itemId] = source;
        }
    }

    /// <summary>
    /// Anything with an equip slot, minus soul crystals. Drops the orchestrion rolls, Triple Triad
    /// cards, materials and job coffers that share the same loot lists.
    /// </summary>
    private static bool IsGlamourableGear(Lumina.Excel.ExcelSheet<Item> items, uint itemId)
    {
        if (!items.TryGetRow(itemId, out var item))
            return false;

        if (item.EquipSlotCategory.RowId == 0 || !item.EquipSlotCategory.IsValid)
            return false;

        return item.EquipSlotCategory.Value.SoulCrystal == 0;
    }

    /// <summary>
    /// Merges the user-maintained overrides file. Additive only - it exists because the upstream
    /// dataset lags badly on the newest dungeons.
    /// </summary>
    private static int ApplyOverrides(
        string configDirectory,
        Dictionary<uint, HashSet<uint>> accumulated,
        Dictionary<uint, Dictionary<uint, LootProvenance>> provenance)
    {
        var path = Path.Combine(configDirectory, OverridesFileName);
        if (!File.Exists(path))
            return 0;

        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, uint[]>>(File.ReadAllText(path));
            if (raw == null)
                return 0;

            var applied = 0;
            foreach (var (key, extra) in raw)
            {
                if (!uint.TryParse(key, out var territoryId))
                    continue;

                if (!accumulated.TryGetValue(territoryId, out var set))
                    accumulated[territoryId] = set = [];

                foreach (var itemId in extra)
                {
                    if (!set.Add(itemId))
                        continue;

                    if (!provenance.TryGetValue(territoryId, out var tagged))
                        provenance[territoryId] = tagged = [];

                    tagged[itemId] = LootProvenance.Override;
                }

                applied++;
            }

            Plugin.Log.Information($"Applied {applied} loot override entries from {path}");
            return applied;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, $"Could not read {path}; ignoring overrides");
            return 0;
        }
    }
}
