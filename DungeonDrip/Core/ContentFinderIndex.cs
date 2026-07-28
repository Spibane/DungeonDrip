using System;
using System.Collections.Generic;
using System.Linq;
using Lumina.Excel.Sheets;

namespace DungeonDrip.Core;

/// <summary>A duty a roulette can drop you into, with the level it wants you at.</summary>
public readonly record struct RouletteDuty(uint TerritoryId, byte Level);

/// <summary>A duty roulette and everything it can send you to.</summary>
/// <param name="RequiredLevel">The level the roulette itself will not queue you below.</param>
public sealed record RoulettePool(string Name, byte RequiredLevel, IReadOnlyList<RouletteDuty> Duties);

/// <summary>
/// Territory -> ContentFinderCondition, built once. Answers what a duty is called and whether it is
/// one this plugin covers.
/// </summary>
/// <remarks>
/// Separate from <see cref="DutyCatalog"/> because that only covers duties we have loot for, and is
/// rebuilt whenever the dataset changes. This covers every duty in the game and never changes.
/// </remarks>
public sealed class ContentFinderIndex
{
    private readonly Dictionary<uint, ContentFinderCondition> byTerritory;

    /// <summary>The roulettes worth advising on, current content first.</summary>
    public IReadOnlyList<RoulettePool> Roulettes { get; }

    private ContentFinderIndex(
        Dictionary<uint, ContentFinderCondition> byTerritory, IReadOnlyList<RoulettePool> roulettes)
    {
        this.byTerritory = byTerritory;
        Roulettes = roulettes;
    }

    private const uint DungeonContentType = 2;
    private const uint RaidContentType = 5;

    /// <summary>
    /// The roulettes we advise on, each paired with the flag that marks a duty as belonging to it.
    /// </summary>
    /// <remarks>
    /// Every duty carries one boolean per roulette, so membership is read off the game's own data
    /// rather than reconstructed from level bands - which matters, because the bands move and the
    /// pools have holes in them (not every level-cap dungeon is in Expert).
    ///
    /// The row id is the ContentRoulette sheet's, used only to read the name the game currently
    /// shows: "Level Cap Dungeons" is renamed every expansion, so the fallback here will rot and
    /// the sheet will not.
    ///
    /// Trials, Main Scenario, Guildhests, Normal Raids, Mentor and the Frontline challenge are
    /// deliberately absent - the plugin only carries loot for dungeons and alliance raids, so it
    /// has nothing to say about the rest.
    /// </remarks>
    private static readonly (uint Row, string Fallback, Func<ContentFinderCondition, bool> Contains)[] RouletteFlags =
    [
        (5,  "Expert",              condition => condition.ExpertRoulette),
        (8,  "Level Cap Dungeons",  condition => condition.LevelCapRoulette),
        (15, "Alliance Raids",      condition => condition.AllianceRoulette),
        (2,  "High-level Dungeons", condition => condition.HighLevelRoulette),
        (1,  "Leveling",            condition => condition.LevelingRoulette),
    ];

    /// <summary>ContentMemberType 4 is the 24-player alliance layout; 8-player raids use 3.</summary>
    private const uint AllianceMemberType = 4;

    public bool TryGet(uint territoryId, out ContentFinderCondition condition) =>
        byTerritory.TryGetValue(territoryId, out condition);

    /// <summary>
    /// Dungeons and alliance raids only - the content whose gear people actually farm for glamour.
    /// </summary>
    /// <remarks>
    /// Trials, 8-player raids, ultimates, guildhests, deep dungeons and the rest are excluded.
    /// Alliance raids share ContentType 5 with 8-player raids, so the party layout is what tells
    /// them apart.
    /// </remarks>
    public bool IsSupportedDuty(uint territoryId) =>
        byTerritory.TryGetValue(territoryId, out var condition) && IsSupported(condition);

    private static bool IsSupported(ContentFinderCondition condition) =>
        condition.ContentType.RowId == DungeonContentType ||
        (condition.ContentType.RowId == RaidContentType &&
         condition.ContentMemberType.RowId == AllianceMemberType);

    public static ContentFinderIndex Build()
    {
        var byTerritory = new Dictionary<uint, ContentFinderCondition>();

        // Keyed by territory so the unrestricted-party duplicates collapse into one entry each.
        var pools = RouletteFlags.Select(_ => new Dictionary<uint, byte>()).ToArray();

        foreach (var row in Plugin.DataManager.GetExcelSheet<ContentFinderCondition>())
        {
            var territory = row.TerritoryType.RowId;
            if (territory == 0 || row.Name.IsEmpty)
                continue;

            // Several rows can point at one territory (unrestricted-party variants and the like);
            // the duty-finder row carries the canonical name.
            if (!byTerritory.TryGetValue(territory, out var existing) ||
                (row.IsInDutyFinder && !existing.IsInDutyFinder))
            {
                byTerritory[territory] = row;
            }

            for (var i = 0; i < RouletteFlags.Length; i++)
            {
                if (RouletteFlags[i].Contains(row))
                    pools[i].TryAdd(territory, row.ClassJobLevelRequired);
            }
        }

        var roulettes = new List<RoulettePool>(RouletteFlags.Length);
        var sheet = Plugin.DataManager.GetExcelSheet<ContentRoulette>();

        for (var i = 0; i < RouletteFlags.Length; i++)
        {
            var (rowId, fallback, _) = RouletteFlags[i];
            var known = sheet.TryGetRow(rowId, out var roulette);

            roulettes.Add(new RoulettePool(
                known ? ShortName(roulette.Name.ExtractText(), fallback) : fallback,
                known ? roulette.RequiredLevel : (byte)0,
                [.. pools[i].Select(entry => new RouletteDuty(entry.Key, entry.Value))]));
        }

        Plugin.Log.Information(
            "Roulette pools: " +
            string.Join(", ", roulettes.Select(pool => $"{pool.Name} {pool.Duties.Count}")));

        return new ContentFinderIndex(byTerritory, roulettes);
    }

    /// <summary>
    /// Drops the "Duty Roulette: " the sheet prefixes every name with - the window says that once,
    /// in its own heading. Falls back rather than returning an empty cell if a name is unexpected.
    /// </summary>
    private static string ShortName(string name, string fallback)
    {
        var colon = name.IndexOf(':');
        var trimmed = colon >= 0 ? name[(colon + 1)..].Trim() : name.Trim();
        return trimmed.Length > 0 ? trimmed : fallback;
    }
}
