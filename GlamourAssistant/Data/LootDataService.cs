using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GlamourAssistant.Data;

public enum LootDataState
{
    /// <summary>Nothing usable yet - first run, or the very first download failed.</summary>
    NoData,

    /// <summary>Talking to the source right now. Cached data may still be usable meanwhile.</summary>
    Checking,

    Ready,
}

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

    private readonly string configDirectory;
    private readonly LearnedLootStore learned;
    private readonly WikiLootSource wiki;
    private readonly HttpClient http;
    private readonly CancellationTokenSource cancellation = new();
    private readonly object sync = new();

    private LootCacheFile? cache;
    private LootCacheFile? pending;
    private Task? inFlight;
    private int seenLearnedRevision = -1;
    private int seenWikiRevision = -1;

    public LootDataService(string configDirectory, LearnedLootStore learned, WikiLootSource wiki)
    {
        this.configDirectory = configDirectory;
        this.learned = learned;
        this.wiki = wiki;

        http = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
        })
        {
            Timeout = TimeSpan.FromSeconds(60),
        };

        http.DefaultRequestHeaders.UserAgent.ParseAdd("GlamourAssistant (Dalamud plugin)");

        LoadCacheFromDisk();
        CheckForUpdates();
    }

    /// <summary>The resolved dataset. Null until the first successful load finishes.</summary>
    public DungeonLootData? Data { get; private set; }

    public LootDataState State { get; private set; } = LootDataState.NoData;

    /// <summary>Human-readable one-liner for the UI.</summary>
    public string StatusMessage { get; private set; } = "Loot data has not been loaded yet.";

    /// <summary>Bumped whenever <see cref="Data"/> is replaced, so callers can rebuild derived state.</summary>
    public int Revision { get; private set; }

    public DateTime? LastCheckedUtc { get; private set; }

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
        LootCacheFile? incoming;
        lock (sync)
        {
            incoming = pending;
            pending = null;
        }

        // A newly observed drop or wiki lookup changes the merged result without changing the
        // download, so rebuild from the cache we already hold.
        var supplementsChanged = learned.Revision != seenLearnedRevision || wiki.Revision != seenWikiRevision;
        if (incoming == null && supplementsChanged && cache != null)
            incoming = cache;

        if (incoming == null)
            return;

        seenLearnedRevision = learned.Revision;
        seenWikiRevision = wiki.Revision;

        try
        {
            Data = DungeonLootData.Build(incoming, configDirectory, learned, wiki);
            State = LootDataState.Ready;
            Revision++;

            Plugin.Log.Information(
                $"Loot data ready: {Data.DutyCount} duties (downloaded {incoming.FetchedUtc:u})");
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Could not build the loot dataset");
            StatusMessage = $"Loot data could not be processed: {ex.Message}";
            State = Data == null ? LootDataState.NoData : LootDataState.Ready;
        }
    }

    public void Dispose()
    {
        cancellation.Cancel();
        cancellation.Dispose();
        http.Dispose();
    }

    private string CachePath => Path.Combine(configDirectory, CacheFileName);

    private void LoadCacheFromDisk()
    {
        if (!File.Exists(CachePath))
            return;

        try
        {
            var loaded = JsonSerializer.Deserialize<LootCacheFile>(File.ReadAllText(CachePath));
            if (loaded == null || loaded.Instances.Count == 0)
                return;

            cache = loaded;
            lock (sync)
                pending = loaded;

            StatusMessage = $"Using loot data downloaded {Describe(loaded.FetchedUtc)}.";
            Plugin.Log.Information($"Loaded cached loot data ({loaded.Instances.Count} duties)");
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, $"Could not read {CachePath}; will download a fresh copy");
        }
    }

    private async Task CheckForUpdatesAsync(bool force, CancellationToken token)
    {
        State = cache == null ? LootDataState.NoData : State;
        StatusMessage = cache == null
            ? "Downloading loot data..."
            : "Checking for loot data updates...";

        var previousState = State;
        State = LootDataState.Checking;

        try
        {
            var existing = cache;
            var sources = await FetchAsync(SourcesUrl, force ? null : existing?.SourcesETag, token);
            var instances = await FetchAsync(InstancesUrl, force ? null : existing?.InstancesETag, token);

            LastCheckedUtc = DateTime.UtcNow;

            if (!sources.Changed && !instances.Changed && existing != null)
            {
                State = previousState;
                StatusMessage = $"Loot data is up to date (downloaded {Describe(existing.FetchedUtc)}).";
                return;
            }

            // Only one of the two changed: we still need the other's body to rebuild the join.
            var sourcesJson = sources.Content ?? (await FetchAsync(SourcesUrl, null, token)).Content;
            var instancesJson = instances.Content ?? (await FetchAsync(InstancesUrl, null, token)).Content;

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

            cache = built;
            SaveCacheToDisk(built);

            lock (sync)
                pending = built;

            StatusMessage = $"Loot data updated ({built.Instances.Count} duties).";
        }
        catch (OperationCanceledException)
        {
            // Plugin is unloading.
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Loot data update failed");
            State = cache == null ? LootDataState.NoData : previousState;
            StatusMessage = cache == null
                ? $"Could not download loot data: {ex.Message}"
                : $"Update failed ({ex.Message}); using data downloaded {Describe(cache.FetchedUtc)}.";
        }
    }

    private async Task<(bool Changed, string? Content, string? ETag)> FetchAsync(
        string url, string? etag, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(etag))
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);

        using var response = await http.SendAsync(request, token);

        if (response.StatusCode == HttpStatusCode.NotModified)
            return (false, null, etag);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(token);
        return (true, body, response.Headers.ETag?.Tag);
    }

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

    private void SaveCacheToDisk(LootCacheFile file)
    {
        try
        {
            Directory.CreateDirectory(configDirectory);
            File.WriteAllText(CachePath, JsonSerializer.Serialize(file));
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, $"Could not write {CachePath}");
        }
    }

    private static string Describe(DateTime utc)
    {
        var age = DateTime.UtcNow - utc;
        return age switch
        {
            { TotalMinutes: < 2 } => "just now",
            { TotalHours: < 1 } => $"{(int)age.TotalMinutes} minutes ago",
            { TotalDays: < 1 } => $"{(int)age.TotalHours} hours ago",
            { TotalDays: < 2 } => "yesterday",
            _ => $"{(int)age.TotalDays} days ago",
        };
    }
}
