using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DungeonDrip.Data;

/// <summary>
/// Owns the dungeon loot dataset: fetches it from upstream on every plugin load, revalidates it
/// with ETags so an unchanged dataset costs one 304, and keeps a copy on disk so the plugin is
/// usable offline and instantly on subsequent launches.
/// </summary>
/// <remarks>
/// The game ships no loot tables, so this comes from FFXIV Teamcraft's public data (which mirrors
/// Garland Tools). The two upstream files are item -> instances and instance metadata; inverting
/// and joining them is what produces our per-duty lists.
/// </remarks>
public sealed class LootDataService : IDisposable
{
    private const string SourceBase =
        "https://raw.githubusercontent.com/ffxiv-teamcraft/ffxiv-teamcraft/staging/libs/data/src/lib/json";

    private const string SourcesUrl = $"{SourceBase}/instance-sources.json";
    private const string InstancesUrl = $"{SourceBase}/instances.json";
    private const string CacheFileName = "dungeon-loot-cache.json";

    /// <summary>instances.json is around 1.4 MB today; the ceiling is slack, not a target.</summary>
    private const int MaxResponseBytes = 16 * 1024 * 1024;

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);

    private readonly string configDirectory;
    private readonly LearnedLootStore learned;
    private readonly WikiLootSource wiki;
    private readonly Core.ContentFinderIndex duties;
    private readonly Core.StorageEligibility storage;
    private readonly HttpFetcher http;
    private readonly CancellationTokenSource cancellation = new();
    private readonly object sync = new();

    private LootCacheFile? cache;
    private LootCacheFile? pending;
    private Task? inFlight;
    private int seenLearnedRevision = -1;
    private int seenWikiRevision = -1;

    public LootDataService(
        string configDirectory,
        LearnedLootStore learned,
        WikiLootSource wiki,
        Core.ContentFinderIndex duties,
        Core.StorageEligibility storage,
        HttpFetcher http)
    {
        this.configDirectory = configDirectory;
        this.learned = learned;
        this.wiki = wiki;
        this.duties = duties;
        this.storage = storage;
        this.http = http;

        LoadCacheFromDisk();
        CheckForUpdates();
    }

    /// <summary>The resolved dataset. Null until the first successful load finishes.</summary>
    public DungeonLootData? Data { get; private set; }

    /// <summary>Human-readable one-liner for the UI.</summary>
    public string StatusMessage { get; private set; } = "Loot data has not been loaded yet.";

    /// <summary>Bumped whenever <see cref="Data"/> is replaced, so callers can rebuild derived state.</summary>
    public int Revision { get; private set; }

    /// <summary>Kicks off an update check unless one is already running.</summary>
    public void CheckForUpdates(bool force = false)
    {
        lock (sync)
        {
            if (inFlight is { IsCompleted: false })
                return;

            inFlight = Task.Run(() => CheckForUpdatesAsync(force, cancellation.Token), cancellation.Token);
        }
    }

    /// <summary>
    /// Framework-thread half of the service: publishes anything the background task downloaded.
    /// Resolving map ids to territories needs Lumina, so it cannot happen off-thread.
    /// </summary>
    public void Update()
    {
        // A newly observed drop or wiki lookup changes the merged result without changing the
        // download, so rebuild from the cache we already hold.
        var supplementsChanged = learned.Revision != seenLearnedRevision || wiki.Revision != seenWikiRevision;

        LootCacheFile? incoming;
        lock (sync)
        {
            incoming = pending ?? (supplementsChanged ? cache : null);
            pending = null;
        }

        if (incoming == null)
            return;

        seenLearnedRevision = learned.Revision;
        seenWikiRevision = wiki.Revision;

        try
        {
            Data = DungeonLootData.Build(incoming, configDirectory, learned, wiki, duties, storage);
            Revision++;

            Plugin.Log.Information(
                $"Loot data ready: {Data.DutyCount} duties (downloaded {incoming.FetchedUtc:u})");
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Could not build the loot dataset");
            StatusMessage = $"Loot data could not be processed: {ex.Message}";
        }
    }

    /// <remarks>
    /// The token source is cancelled but not disposed: a request may still be unwinding on it, and
    /// disposing underneath that throws. It holds no timer or wait handle, so there is nothing to
    /// release. The client is owned by the plugin, not by this.
    /// </remarks>
    public void Dispose() => cancellation.Cancel();

    private string CachePath => Path.Combine(configDirectory, CacheFileName);

    private void LoadCacheFromDisk()
    {
        var loaded = JsonStore.Read<LootCacheFile>(CachePath);
        if (loaded == null || loaded.Instances.Count == 0)
            return;

        lock (sync)
            cache = pending = loaded;

        StatusMessage = $"Using loot data downloaded {Core.Format.Age(loaded.FetchedUtc)} ago.";
        Plugin.Log.Information($"Loaded cached loot data ({loaded.Instances.Count} duties)");
    }

    private async Task CheckForUpdatesAsync(bool force, CancellationToken token)
    {
        StatusMessage = cache == null
            ? "Downloading loot data..."
            : "Checking for loot data updates...";

        try
        {
            var existing = cache;
            var sources = await Fetch(SourcesUrl, force ? null : existing?.SourcesETag, token);
            var instances = await Fetch(InstancesUrl, force ? null : existing?.InstancesETag, token);

            if (sources.NotModified && instances.NotModified && existing != null)
            {
                StatusMessage = $"Loot data is up to date (downloaded {Core.Format.Age(existing.FetchedUtc)} ago).";
                return;
            }

            // Only one of the two changed: we still need the other's body to rebuild the join.
            var sourcesJson = sources.Body ?? (await Fetch(SourcesUrl, null, token)).Body;
            var instancesJson = instances.Body ?? (await Fetch(InstancesUrl, null, token)).Body;

            if (sourcesJson == null || instancesJson == null)
                throw new InvalidOperationException("upstream returned no body");

            var built = new LootCacheFile
            {
                FetchedUtc = DateTime.UtcNow,
                SourcesETag = sources.ETag ?? existing?.SourcesETag,
                InstancesETag = instances.ETag ?? existing?.InstancesETag,
                Instances = Transform(instancesJson, sourcesJson),
            };

            if (built.Instances.Count == 0)
                throw new InvalidOperationException("upstream data produced no duties");

            JsonStore.Write(CachePath, built);

            // Under the same lock as pending: this runs on a worker and both are read by the
            // framework thread, so publishing one without the other is a torn hand-off.
            lock (sync)
                cache = pending = built;

            StatusMessage = $"Loot data updated ({built.Instances.Count} duties).";
        }
        catch (OperationCanceledException)
        {
            // Plugin is unloading.
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Loot data update failed");
            StatusMessage = cache == null
                ? $"Could not download loot data: {ex.Message}"
                : $"Update failed ({ex.Message}); using data downloaded {Core.Format.Age(cache.FetchedUtc)} ago.";
        }
    }

    private Task<HttpFetcher.Response> Fetch(string url, string? etag, CancellationToken token) =>
        http.GetAsync(url, etag, RequestTimeout, MaxResponseBytes, token);

    /// <summary>
    /// Inverts item -> instances into instance -> items and attaches each instance's map id.
    /// </summary>
    /// <remarks>
    /// Everything here is defensive about the incoming shape. The upstream files carry a handful of
    /// negative pseudo-instance ids, and this runs unattended on every plugin load, so a value that
    /// does not fit gets skipped rather than aborting the whole dataset.
    /// </remarks>
    private static List<LootInstance> Transform(string instancesJson, string sourcesJson)
    {
        var sources = JsonSerializer.Deserialize<Dictionary<string, long[]>>(sourcesJson) ?? [];
        var rawMetadata = JsonSerializer.Deserialize<Dictionary<string, TeamcraftInstance>>(instancesJson) ?? [];

        var metadata = new Dictionary<uint, TeamcraftInstance>(rawMetadata.Count);
        foreach (var (key, value) in rawMetadata)
        {
            if (uint.TryParse(key, out var instanceId))
                metadata[instanceId] = value;
        }

        var byInstance = new Dictionary<uint, HashSet<uint>>();
        foreach (var (key, instanceIds) in sources)
        {
            if (!uint.TryParse(key, out var itemId))
                continue;

            foreach (var rawInstanceId in instanceIds)
            {
                if (rawInstanceId is <= 0 or > uint.MaxValue)
                    continue;

                var instanceId = (uint)rawInstanceId;
                if (!byInstance.TryGetValue(instanceId, out var items))
                    byInstance[instanceId] = items = [];

                items.Add(itemId);
            }
        }

        var result = new List<LootInstance>(byInstance.Count);
        foreach (var (instanceId, items) in byInstance)
        {
            if (!metadata.TryGetValue(instanceId, out var meta))
                continue;

            if (meta.Map is not (> 0 and <= uint.MaxValue))
                continue;

            result.Add(new LootInstance
            {
                Id = instanceId,
                Map = (uint)meta.Map.Value,
                Name = string.IsNullOrWhiteSpace(meta.En) ? $"Instance {instanceId}" : meta.En,
                Items = [.. items.Order()],
            });
        }

        result.Sort((a, b) => a.Id.CompareTo(b.Id));
        return result;
    }

}
