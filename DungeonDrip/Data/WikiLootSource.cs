using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DungeonDrip.Data;

/// <summary>What the wiki had to say about one duty, and when it was last asked.</summary>
public sealed class WikiEntry
{
    /// <summary>The page actually read, after redirects. The next lookup starts from this.</summary>
    public string? Title { get; set; }

    /// <summary>The page revision this was parsed from, so an unchanged page can skip the parse.</summary>
    public long RevisionId { get; set; }

    /// <summary>When the wiki was last asked, whatever it said. Drives the TTLs above.</summary>
    public DateTime CheckedUtc { get; set; }

    /// <summary>No page could be matched. Cached so a miss is not retried on every window open.</summary>
    public bool NotFound { get; set; }

    /// <summary>Last failure, if the attempt errored rather than simply finding nothing.</summary>
    public string? Error { get; set; }

    public uint[] Items { get; set; } = [];

    /// <summary>Listed names that matched no item row - the signal that parsing has drifted.</summary>
    public int UnmatchedNames { get; set; }

    /// <summary>
    /// The same items again, split by the boss or coffer whose table they were listed under.
    /// </summary>
    /// <remarks>
    /// Kept alongside <see cref="Items"/> rather than replacing it. That one is the flat union the
    /// loot merge wants and the only thing that decides whether a piece is listed at all; this is
    /// extra detail on top, and it is normal for it to be empty - a page whose drops sit directly
    /// under one heading attributes nothing, and every duty the wiki was never asked about has none
    /// of this either.
    /// </remarks>
    public LootAttribution[] Attributions { get; set; } = [];
}

/// <summary>
/// Enriches a duty's loot list from the FFXIV Console Games Wiki, lazily and one duty at a time.
/// </summary>
/// <remarks>
/// The primary dataset (Teamcraft, mirroring Garland) lags months behind on new dungeons - Mistwake
/// listed 2 drops and the Clyteum 1 while the wiki had full tables for both. This fills that gap
/// without replacing the primary source: it is strictly additive, per-duty, and every failure mode
/// degrades to "keep whatever was already there".
///
/// The wiki lists item *names*, which are resolved against the Item sheet, so a name the game does
/// not know is dropped and counted rather than guessed at.
/// </remarks>
public sealed class WikiLootSource : IDisposable
{
    /// <summary>
    /// The wiki's API. The host is shared with the article links so the two cannot drift apart.
    /// </summary>
    private const string ApiUrl = Core.Sources.ItemLink.ConsoleGamesWikiHost + "/mediawiki/api.php";
    private const string CacheFileName = "wiki-loot-cache.json";

    /// <summary>How long a successful lookup is trusted before re-checking the page revision.</summary>
    private static readonly TimeSpan SuccessTtl = TimeSpan.FromDays(14);

    /// <summary>Misses are cached too, so an undocumented duty is not re-fetched constantly.</summary>
    private static readonly TimeSpan MissTtl = TimeSpan.FromDays(3);

    /// <summary>Politeness floor between requests to a community-run wiki.</summary>
    private static readonly TimeSpan MinRequestInterval = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    private const int MaxResponseBytes = 4 * 1024 * 1024;

    private readonly string path;
    private readonly Configuration configuration;
    private readonly HttpFetcher http;
    private readonly CancellationTokenSource cancellation = new();
    private readonly object sync = new();

    private readonly Dictionary<uint, WikiEntry> byTerritory = [];
    private readonly List<(uint Territory, WikiEntry Entry)> pending = [];

    private Task? inFlight;
    private DateTime nextRequestAllowed = DateTime.MinValue;

    public WikiLootSource(string configDirectory, Configuration configuration, HttpFetcher http)
    {
        path = Path.Combine(configDirectory, CacheFileName);
        this.configuration = configuration;
        this.http = http;

        Load();
    }

    /// <summary>Bumped when a lookup lands, so the merged dataset can be rebuilt.</summary>
    public int Revision { get; private set; }

    public bool IsBusy
    {
        get { lock (sync) return inFlight is { IsCompleted: false }; }
    }

    public int DutiesWithData => byTerritory.Count(kv => kv.Value.Items.Length > 0);

    public int TotalItems => byTerritory.Sum(kv => kv.Value.Items.Length);

    public IReadOnlyDictionary<uint, WikiEntry> All => byTerritory;

    public WikiEntry? EntryFor(uint territoryId) =>
        byTerritory.TryGetValue(territoryId, out var entry) ? entry : null;

    /// <summary>
    /// Framework thread. Starts a lookup if this duty has no fresh answer and nothing else is
    /// already running.
    /// </summary>
    public void RequestIfStale(uint territoryId, string dutyName, bool force = false)
    {
        if (!configuration.UseWikiSource || territoryId == 0 || string.IsNullOrWhiteSpace(dutyName))
            return;

        if (!force && !NeedsLookup(territoryId))
            return;

        lock (sync)
        {
            if (inFlight is { IsCompleted: false })
                return;

            if (DateTime.UtcNow < nextRequestAllowed)
                return;

            nextRequestAllowed = DateTime.UtcNow + MinRequestInterval;

            // The name index must be built here, on the framework thread, before handing it off.
            var names = Core.ItemNameIndex.Get();
            var known = EntryFor(territoryId);

            inFlight = Task.Run(
                () => LookupAsync(territoryId, dutyName, known, names, cancellation.Token),
                cancellation.Token);
        }
    }

    /// <summary>Framework thread. Applies anything the background lookup produced.</summary>
    public void Update()
    {
        List<(uint Territory, WikiEntry Entry)> drained;
        lock (sync)
        {
            if (pending.Count == 0)
                return;

            drained = [.. pending];
            pending.Clear();
        }

        foreach (var (territory, entry) in drained)
            byTerritory[territory] = entry;

        Revision++;
        Save();
    }

    public void Clear()
    {
        if (byTerritory.Count == 0)
            return;

        byTerritory.Clear();
        Revision++;
        Save();
    }

    /// <remarks>See <see cref="LootDataService.Dispose"/> - cancelled, not disposed.</remarks>
    public void Dispose() => cancellation.Cancel();

    private bool NeedsLookup(uint territoryId)
    {
        if (!byTerritory.TryGetValue(territoryId, out var entry))
            return true;

        var age = DateTime.UtcNow - entry.CheckedUtc;
        return entry.NotFound || entry.Error != null ? age > MissTtl : age > SuccessTtl;
    }

    /// <summary>
    /// The whole lookup for one duty, on a worker thread: find the page, read it, parse it, publish.
    /// </summary>
    /// <remarks>
    /// Written so that every exit produces an entry to cache, including the failures. A miss and an
    /// error are both recorded with their own shorter lifetime, which is what stops a duty with no
    /// page being re-fetched on every window open; only cancellation records nothing, because that is
    /// the plugin unloading rather than an answer.
    ///
    /// The revision check before the parse is the reason a fortnightly refresh is cheap: an unchanged
    /// page costs one request and keeps the parsed result.
    /// </remarks>
    private async Task LookupAsync(
        uint territoryId,
        string dutyName,
        WikiEntry? known,
        IReadOnlyDictionary<string, uint> names,
        CancellationToken token)
    {
        var entry = new WikiEntry { CheckedUtc = DateTime.UtcNow, Title = known?.Title };

        try
        {
            var title = known?.Title ?? await ResolveTitleAsync(dutyName, token);
            if (title == null)
            {
                entry.NotFound = true;
                Plugin.Log.Information($"Wiki: no page matches \"{dutyName}\"");
                Publish(territoryId, entry);
                return;
            }

            entry.Title = title;

            // Cheap revision check first: if the page has not changed, keep the parsed result and
            // just refresh the timestamp.
            var revisionId = await GetRevisionIdAsync(title, token);
            if (known != null && revisionId != 0 && revisionId == known.RevisionId && known.Items.Length > 0)
            {
                entry.RevisionId = revisionId;
                entry.Items = known.Items;
                entry.UnmatchedNames = known.UnmatchedNames;
                entry.Attributions = known.Attributions;
                Publish(territoryId, entry);
                return;
            }

            var page = await GetWikitextAsync(title, token);
            if (page == null)
            {
                entry.NotFound = true;
                Publish(territoryId, entry);
                return;
            }

            // Store the post-redirect title so later checks go straight to the real article.
            entry.Title = page.Value.Title;

            var (items, unmatched, attributions) = WikiDropTables.Parse(page.Value.Text, names);

            var elsewhere = await FollowToDutyPageAsync(entry.Title, items, unmatched, names, token);
            if (elsewhere != null)
            {
                entry.Title = elsewhere.Value.Title;
                (items, unmatched, attributions) = elsewhere.Value.Parsed;

                // The revision belongs to whichever page was read, and the next lookup starts from
                // the title stored here - so it has to be the article's, not the signpost's.
                revisionId = await GetRevisionIdAsync(entry.Title, token);
            }

            entry.RevisionId = revisionId;
            entry.Items = items;
            entry.UnmatchedNames = unmatched;
            entry.Attributions = attributions;

            Plugin.Log.Information(
                $"Wiki: \"{entry.Title}\" -> {items.Length} gear items" +
                (attributions.Length > 0 ? $" across {attributions.Length} bosses and coffers" : string.Empty) +
                (unmatched > 0 ? $" ({unmatched} listed names matched no item)" : string.Empty));

            Publish(territoryId, entry);
        }
        catch (OperationCanceledException)
        {
            // Unloading, or the request timed out. Nothing recorded, so it retries later.
        }
        catch (Exception ex)
        {
            // Recorded rather than swallowed, so a persistently broken duty backs off instead of
            // retrying on every window open.
            entry.Error = ex.Message;
            Plugin.Log.Warning(ex, $"Wiki lookup failed for \"{dutyName}\"");
            Publish(territoryId, entry);
        }
    }

    private void Publish(uint territoryId, WikiEntry entry)
    {
        lock (sync)
            pending.Add((territoryId, entry));
    }

    /// <summary>
    /// Duty names do not always match page titles exactly, so try the obvious spellings before
    /// falling back to the wiki's own search.
    /// </summary>
    private async Task<string?> ResolveTitleAsync(string dutyName, CancellationToken token)
    {
        foreach (var candidate in TitleCandidates(dutyName))
        {
            if (await PageExistsAsync(candidate, token))
                return candidate;
        }

        return await SearchTitleAsync(dutyName, token);
    }

    private static IEnumerable<string> TitleCandidates(string dutyName)
    {
        var trimmed = dutyName.Trim();
        if (trimmed.Length == 0)
            yield break;

        // MediaWiki capitalises the first letter itself, but doing it here keeps the cached title
        // matching what the API reports back.
        var capitalised = char.ToUpperInvariant(trimmed[0]) + trimmed[1..];
        yield return capitalised;

        // ContentFinderCondition uses typographic dashes and apostrophes; the wiki often does not.
        var plain = capitalised.Replace('–', '-').Replace('—', '-').Replace('’', '\'');
        if (plain != capitalised)
            yield return plain;
    }

    private async Task<bool> PageExistsAsync(string title, CancellationToken token)
    {
        var json = await GetJsonAsync(
            $"?action=query&format=json&formatversion=2&redirects=1&titles={Uri.EscapeDataString(title)}", token);

        if (json == null)
            return false;

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("query", out var query) ||
            !query.TryGetProperty("pages", out var pages) || pages.GetArrayLength() == 0)
        {
            return false;
        }

        var page = pages[0];
        return !(page.TryGetProperty("missing", out var missing) && missing.GetBoolean());
    }

    private async Task<string?> SearchTitleAsync(string dutyName, CancellationToken token)
    {
        var json = await GetJsonAsync(
            $"?action=query&format=json&formatversion=2&list=search&srlimit=3&srsearch={Uri.EscapeDataString(dutyName)}",
            token);

        if (json == null)
            return null;

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("query", out var query) ||
            !query.TryGetProperty("search", out var results) || results.GetArrayLength() == 0)
        {
            return null;
        }

        // Only accept a search hit that really is the duty; "Mapping the Realm: X" and similar
        // companion pages rank highly and carry no loot table.
        var wanted = Normalise(dutyName);
        foreach (var result in results.EnumerateArray())
        {
            var title = result.GetProperty("title").GetString();
            if (title != null && Normalise(title) == wanted)
                return title;
        }

        return null;
    }

    /// <summary>The wiki's suffix for a duty whose plain name is taken by something else.</summary>
    private const string DutySuffix = " (Duty)";

    /// <summary>
    /// Second look at "<c>&lt;name&gt; (Duty)</c>" when the page landed on carries no drop table.
    /// </summary>
    /// <remarks>
    /// A duty named after the place it happens in does not own its own title. "Ala Mhigo" is the
    /// city, "Alzadaal's Legacy" and "the Fell Court of Troia" are disambiguation pages, and all
    /// three duties sit at the suffixed title with a full set of per-boss tables on them. Nothing
    /// distinguishes those pages from a duty page cheaply - one is a hatnote, the others a template -
    /// so the trigger is the outcome rather than the shape: a page that yielded no drop table at all
    /// is not the page being looked for, whatever it is.
    ///
    /// <para>Unmatched names count as a drop table. A real duty page whose item names have drifted
    /// out of the game's spelling is a parsing problem to be seen in the count, not a reason to go
    /// looking for a different article.</para>
    ///
    /// <para>One hop, and only when there is nothing to lose by it. Three duties in the game need
    /// this and a handful more - the Praetorium, Castrum Meridianum - genuinely drop no gear, so they
    /// pay one request that finds no page each time their fortnight is up. That is the whole cost of
    /// being wrong here.</para>
    /// </remarks>
    private async Task<(string Title, (uint[] Items, int Unmatched, LootAttribution[] Attributions) Parsed)?>
        FollowToDutyPageAsync(
            string title,
            uint[] items,
            int unmatched,
            IReadOnlyDictionary<string, uint> names,
            CancellationToken token)
    {
        if (items.Length > 0 || unmatched > 0 || title.EndsWith(DutySuffix, StringComparison.OrdinalIgnoreCase))
            return null;

        var page = await GetWikitextAsync(title + DutySuffix, token);
        if (page == null)
            return null;

        var parsed = WikiDropTables.Parse(page.Value.Text, names);
        if (parsed.Items.Length == 0)
            return null;

        Plugin.Log.Information($"Wiki: \"{title}\" carries no drop table; read \"{page.Value.Title}\" instead");
        return (page.Value.Title, parsed);
    }

    private static string Normalise(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private async Task<long> GetRevisionIdAsync(string title, CancellationToken token)
    {
        // redirects=1 matters: without it this reports the revision of the redirect stub, which
        // never changes when the real page is edited.
        var json = await GetJsonAsync(
            $"?action=query&format=json&formatversion=2&redirects=1&prop=revisions&rvprop=ids" +
            $"&titles={Uri.EscapeDataString(title)}",
            token);

        if (json == null)
            return 0;

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("query", out var query) ||
            !query.TryGetProperty("pages", out var pages) || pages.GetArrayLength() == 0)
        {
            return 0;
        }

        if (!pages[0].TryGetProperty("revisions", out var revisions) || revisions.GetArrayLength() == 0)
            return 0;

        return revisions[0].TryGetProperty("revid", out var revid) ? revid.GetInt64() : 0;
    }

    /// <summary>
    /// Fetches a page's source, following redirects, and reports the title actually landed on.
    /// </summary>
    /// <remarks>
    /// action=parse does NOT follow redirects unless asked, and several duties are redirects -
    /// ContentFinderCondition spells "Toto–Rak" with an en dash while the article uses a hyphen.
    /// Without redirects=1 those pages return "#REDIRECT [[...]]" and parse as having no loot at all.
    /// </remarks>
    private async Task<(string Title, string Text)?> GetWikitextAsync(string title, CancellationToken token)
    {
        var json = await GetJsonAsync(
            $"?action=parse&format=json&formatversion=2&redirects=1&prop=wikitext" +
            $"&page={Uri.EscapeDataString(title)}",
            token);

        if (json == null)
            return null;

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("parse", out var parse) ||
            !parse.TryGetProperty("wikitext", out var wikitext))
        {
            return null;
        }

        var text = wikitext.GetString();
        if (text == null)
            return null;

        var resolved = parse.TryGetProperty("title", out var parsedTitle)
            ? parsedTitle.GetString() ?? title
            : title;

        return (resolved, text);
    }

    private async Task<string?> GetJsonAsync(string queryString, CancellationToken token) =>
        (await http.GetAsync(ApiUrl + queryString, null, RequestTimeout, MaxResponseBytes, token)).Body;

    private void Load()
    {
        foreach (var (territoryId, entry) in JsonStore.ReadByTerritory<WikiEntry>(path))
            byTerritory[territoryId] = entry;

        if (byTerritory.Count > 0)
            Plugin.Log.Information($"Loaded wiki loot cache: {DutiesWithData} duties, {TotalItems} items");
    }

    private void Save() => JsonStore.WriteByTerritory(path, byTerritory);
}
