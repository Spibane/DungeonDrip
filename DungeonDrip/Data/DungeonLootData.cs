using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lumina.Excel.Sheets;

namespace DungeonDrip.Data;

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
    private const string OverridesFileName = "loot-overrides.json";

    private static readonly DropOrigin[] NoOrigins = [];

    private readonly Dictionary<uint, uint[]> itemsByTerritory;
    private readonly Dictionary<uint, string> fallbackNames;
    private readonly Dictionary<uint, Dictionary<uint, LootProvenance>> provenance;
    private readonly Dictionary<uint, Dictionary<uint, DropOrigin[]>> origins;

    /// <summary>When the underlying dataset was downloaded.</summary>
    public DateTime FetchedUtc { get; }

    private DungeonLootData(
        Dictionary<uint, uint[]> itemsByTerritory,
        Dictionary<uint, string> fallbackNames,
        Dictionary<uint, Dictionary<uint, LootProvenance>> provenance,
        Dictionary<uint, Dictionary<uint, DropOrigin[]>> origins,
        DateTime fetchedUtc)
    {
        this.itemsByTerritory = itemsByTerritory;
        this.fallbackNames = fallbackNames;
        this.provenance = provenance;
        this.origins = origins;
        FetchedUtc = fetchedUtc;
    }

    /// <summary>Where this piece's entry came from, for showing the user why it is listed.</summary>
    public LootProvenance ProvenanceOf(uint territoryId, uint itemId) =>
        provenance.TryGetValue(territoryId, out var items) && items.TryGetValue(itemId, out var source)
            ? source
            : LootProvenance.Dataset;

    /// <summary>
    /// Which bosses or coffers in a duty drop a piece. Empty when nothing knows.
    /// </summary>
    /// <remarks>
    /// <b>Empty is the common case and never means "nowhere in this duty".</b> Only the wiki says
    /// where inside a duty a piece comes from, and it is asked one duty at a time, only about duties that
    /// have been opened - so a duty the downloaded dataset covers on its own has no attribution at all,
    /// and a duty that has been looked up can still have pieces the dataset knows and the wiki's
    /// tables do not list. Anything drawing this has to be able to say nothing rather than guess.
    /// </remarks>
    public IReadOnlyList<DropOrigin> OriginsOf(uint territoryId, uint itemId) =>
        origins.TryGetValue(territoryId, out var byItem) && byItem.TryGetValue(itemId, out var found)
            ? found
            : NoOrigins;

    /// <summary>Whether anything in this duty is attributed, so a UI can say why it is not.</summary>
    public bool HasOrigins(uint territoryId) => origins.ContainsKey(territoryId);

    public IReadOnlyCollection<uint> Territories => itemsByTerritory.Keys;

    public int DutyCount => itemsByTerritory.Count;

    public bool TryGetItems(uint territoryId, out uint[] items) =>
        itemsByTerritory.TryGetValue(territoryId, out items!);

    public string? GetFallbackName(uint territoryId) =>
        fallbackNames.TryGetValue(territoryId, out var name) ? name : null;

    /// <summary>
    /// Joins every source into one duty -> items map, resolving map ids to territories on the way.
    /// </summary>
    /// <remarks>
    /// Layered in order of authority, and every layer is additive: the downloaded dataset, then the
    /// hand-written overrides, then the wiki, then what has been seen dropping. Nothing ever removes
    /// a piece another source listed, so a bad supplementary source can only over-list - which is the
    /// direction a collection tool should fail in.
    ///
    /// Each layer records the pieces that exist only because of it, which is what lets a UI say why
    /// something is listed. A piece the dataset already had keeps its original provenance however
    /// many later sources also mention it.
    /// </remarks>
    public static DungeonLootData Build(
        LootCacheFile cache,
        string configDirectory,
        LearnedLootStore learned,
        WikiLootSource wiki,
        Core.ContentFinderIndex duties,
        Core.StorageEligibility storage)
    {
        var maps = Plugin.DataManager.GetExcelSheet<Map>();
        var accumulated = new Dictionary<uint, HashSet<uint>>();
        var names = new Dictionary<uint, string>();

        foreach (var instance in cache.Instances)
        {
            if (!maps.TryGetRow(instance.Map, out var map))
                continue;

            var territoryId = map.TerritoryType.RowId;
            if (territoryId == 0)
                continue;

            // Dungeons and alliance raids only.
            if (!duties.IsSupportedDuty(territoryId))
                continue;

            var gear = instance.Items.Where(storage.CanBeStored).ToList();
            if (gear.Count == 0)
                continue;

            if (!accumulated.TryGetValue(territoryId, out var set))
                accumulated[territoryId] = set = [];

            set.UnionWith(gear);
            names.TryAdd(territoryId, instance.Name);
        }

        var provenance = new Dictionary<uint, Dictionary<uint, LootProvenance>>();
        ApplyOverrides(configDirectory, accumulated, provenance);

        // Supplementary sources are layered on last. Each records which pieces only exist because
        // of it, so the UI can show provenance - and because they can add a duty the primary
        // dataset omits entirely, they use the same "create the territory if absent" path.
        foreach (var (territoryId, entry) in wiki.All)
            Merge(duties, storage, accumulated, provenance, territoryId, entry.Items, LootProvenance.Wiki);

        foreach (var (territoryId, seen) in learned.All)
            Merge(duties, storage, accumulated, provenance, territoryId, seen, LootProvenance.Learned);

        var byTerritory = accumulated
            .Where(kv => kv.Value.Count > 0)
            .ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());

        return new DungeonLootData(
            byTerritory, names, provenance, BuildOrigins(wiki, duties, storage, accumulated), cache.FetchedUtc);
    }

    /// <summary>
    /// Inverts the wiki's per-boss tables into piece -> the bosses and coffers that drop it.
    /// </summary>
    /// <remarks>
    /// Independent of provenance, which answers a different question: a piece the downloaded dataset
    /// already listed still gets its attribution if the wiki's tables happen to say where it comes
    /// from. The two are only ever read together by the tooltip, which prints both.
    ///
    /// Held to the merged list rather than taking the wiki's word for it, so a piece filtered out for
    /// being unstorable - or listed under a duty the plugin does not cover - cannot arrive here as an
    /// attribution for a piece no list contains.
    /// </remarks>
    private static Dictionary<uint, Dictionary<uint, DropOrigin[]>> BuildOrigins(
        WikiLootSource wiki,
        Core.ContentFinderIndex duties,
        Core.StorageEligibility storage,
        Dictionary<uint, HashSet<uint>> merged)
    {
        var origins = new Dictionary<uint, Dictionary<uint, DropOrigin[]>>();

        foreach (var (territoryId, entry) in wiki.All)
        {
            if (entry.Attributions.Length == 0 || !duties.IsSupportedDuty(territoryId))
                continue;

            if (!merged.TryGetValue(territoryId, out var listed))
                continue;

            var accumulated = new Dictionary<uint, List<DropOrigin>>();

            foreach (var attribution in entry.Attributions)
            {
                var origin = new DropOrigin(attribution.Label, attribution.Order);

                foreach (var itemId in attribution.Items)
                {
                    if (!listed.Contains(itemId) || !storage.CanBeStored(itemId))
                        continue;

                    if (!accumulated.TryGetValue(itemId, out var found))
                        accumulated[itemId] = found = [];

                    found.Add(origin);
                }
            }

            if (accumulated.Count > 0)
                origins[territoryId] = accumulated.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
        }

        var attributed = origins.Sum(kv => kv.Value.Count);
        if (attributed > 0)
            Plugin.Log.Information($"Attributed {attributed} pieces to a boss or coffer across {origins.Count} duties");

        return origins;
    }

    /// <summary>
    /// Adds a supplementary source's items, tagging only the ones it actually contributed. An item
    /// the primary dataset already listed keeps its original provenance.
    /// </summary>
    private static void Merge(
        Core.ContentFinderIndex duties,
        Core.StorageEligibility storage,
        Dictionary<uint, HashSet<uint>> accumulated,
        Dictionary<uint, Dictionary<uint, LootProvenance>> provenance,
        uint territoryId,
        IEnumerable<uint> itemIds,
        LootProvenance source)
    {
        if (!duties.IsSupportedDuty(territoryId))
            return;

        foreach (var itemId in itemIds)
        {
            if (!storage.CanBeStored(itemId))
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
    /// Merges the user-maintained overrides file. Additive only - it exists because the upstream
    /// dataset lags badly on the newest dungeons.
    /// </summary>
    private static void ApplyOverrides(
        string configDirectory,
        Dictionary<uint, HashSet<uint>> accumulated,
        Dictionary<uint, Dictionary<uint, LootProvenance>> provenance)
    {
        var path = Path.Combine(configDirectory, OverridesFileName);
        var raw = JsonStore.ReadByTerritory<uint[]>(path);
        if (raw.Count == 0)
            return;

        var applied = 0;
        foreach (var (territoryId, extra) in raw)
        {
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
    }
}
