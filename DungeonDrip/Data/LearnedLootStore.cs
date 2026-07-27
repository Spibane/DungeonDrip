using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DungeonDrip.Data;

/// <summary>
/// Drops this client has actually watched fall, recorded per territory.
/// </summary>
/// <remarks>
/// The upstream dataset lags badly on brand-new dungeons - often by months - so this fills the gap
/// with first-hand evidence. Deliberately written in the same shape as loot-overrides.json, so an
/// entry learned here can be promoted to a hand-maintained override, or contributed upstream, by
/// copying it across.
/// </remarks>
public sealed class LearnedLootStore
{
    private const string FileName = "learned-loot.json";

    private readonly string path;
    private readonly Dictionary<uint, HashSet<uint>> byTerritory = [];

    public LearnedLootStore(string configDirectory)
    {
        path = Path.Combine(configDirectory, FileName);
        Load();
    }

    /// <summary>Bumped on every genuinely new sighting, so the dataset can be rebuilt.</summary>
    public int Revision { get; private set; }

    public int TerritoryCount => byTerritory.Count;

    public int ItemCount => byTerritory.Values.Sum(items => items.Count);

    public IReadOnlyDictionary<uint, HashSet<uint>> All => byTerritory;

    /// <summary>Records a sighting. Returns true only the first time an item is seen in a duty.</summary>
    public bool Add(uint territoryId, uint itemId)
    {
        if (!byTerritory.TryGetValue(territoryId, out var items))
            byTerritory[territoryId] = items = [];

        if (!items.Add(itemId))
            return false;

        Revision++;
        Save();
        return true;
    }

    public void Clear()
    {
        if (byTerritory.Count == 0)
            return;

        byTerritory.Clear();
        Revision++;
        Save();
    }

    private void Load()
    {
        var raw = JsonStore.Read<Dictionary<string, uint[]>>(path);
        if (raw == null)
            return;

        foreach (var (key, items) in raw)
        {
            if (uint.TryParse(key, out var territoryId))
                byTerritory[territoryId] = [.. items];
        }

        Plugin.Log.Information($"Loaded {ItemCount} learned drops across {TerritoryCount} duties");
    }

    private void Save() =>
        JsonStore.Write(
            path,
            byTerritory.Where(kv => kv.Value.Count > 0)
                       .ToDictionary(kv => kv.Key.ToString(), kv => kv.Value.Order().ToArray()),
            indented: true);
}
