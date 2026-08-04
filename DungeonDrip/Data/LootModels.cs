using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DungeonDrip.Data;

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

/// <summary>
/// One boss or coffer inside a duty, and what it drops.
/// </summary>
/// <remarks>
/// Which of the two it is is not recorded. The label is read off the wiki's own heading and already
/// says - "Treasure Coffer 2" is not mistakable for a boss - and the alternative was classifying by
/// looking for the word "coffer" in a name, which would have been wrong about the first boss called
/// one and right about nothing the reader could not already see.
/// </remarks>
public sealed class LootAttribution
{
    [JsonPropertyName("label")] public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Where this appeared on the page it was read from.
    /// </summary>
    /// <remarks>
    /// So a grouped list can run in the order they are met rather than alphabetically. Coffers land
    /// after the bosses because that is how the pages are laid out, which is also the order that is
    /// useful to read.
    /// </remarks>
    [JsonPropertyName("order")] public int Order { get; set; }

    [JsonPropertyName("items")] public uint[] Items { get; set; } = [];
}

/// <summary>
/// Where inside a duty one piece drops, as the UI reads it.
/// </summary>
/// <remarks>
/// A piece can have more than one of these - the same accessory is often in two coffers, and a couple
/// of duties list a piece under both a boss and a coffer - so this is always handed out as a list.
/// </remarks>
public sealed record DropOrigin(string Label, int Order);

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
/// Shape of the upstream instances.json entries - only the fields needed, and deliberately loose
/// about them: this is third-party data parsed on every plugin load, so a stray negative or missing
/// value must not take the plugin down.
/// </summary>
internal sealed class TeamcraftInstance
{
    [JsonPropertyName("map")] public long? Map { get; set; }
    [JsonPropertyName("en")] public string? En { get; set; }
}
