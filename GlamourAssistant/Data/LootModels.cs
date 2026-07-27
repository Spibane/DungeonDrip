using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GlamourAssistant.Data;

/// <summary>Which source put a piece on a duty's list.</summary>
public enum LootProvenance
{
    /// <summary>The downloaded primary dataset.</summary>
    Dataset,

    /// <summary>The user's hand-maintained loot-overrides.json.</summary>
    Override,

    /// <summary>Observed dropping in that duty on this client.</summary>
    Learned,

    /// <summary>Read off the FFXIV Console Games Wiki.</summary>
    Wiki,
}

/// <summary>One duty's drop list, keyed on the map id it was sourced from.</summary>
/// <remarks>
/// Map rather than ContentFinderCondition because the upstream dataset numbers instances its own
/// way; the map id survives translation to a territory through Lumina.
/// </remarks>
public sealed class LootInstance
{
    [JsonPropertyName("id")] public uint Id { get; set; }
    [JsonPropertyName("map")] public uint Map { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("items")] public uint[] Items { get; set; } = [];
}

/// <summary>The downloaded dataset as it sits on disk between sessions.</summary>
public sealed class LootCacheFile
{
    /// <summary>When the plugin last actually downloaded (as opposed to revalidated) the data.</summary>
    public DateTime FetchedUtc { get; set; }

    /// <summary>ETags of the upstream files, so the next launch can ask "changed?" instead of refetching.</summary>
    public string? InstancesETag { get; set; }

    public string? SourcesETag { get; set; }

    public List<LootInstance> Instances { get; set; } = [];
}

/// <summary>
/// Shape of the upstream instances.json entries - only the fields we need, and deliberately loose
/// about them: this is third-party data parsed on every plugin load, so a stray negative or missing
/// value must not take the plugin down.
/// </summary>
internal sealed class TeamcraftInstance
{
    [JsonPropertyName("map")] public long? Map { get; set; }
    [JsonPropertyName("en")] public string? En { get; set; }
}
