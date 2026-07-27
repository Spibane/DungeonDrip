using System.Collections.Generic;
using Lumina.Excel.Sheets;

namespace GlamourAssistant.Core;

/// <summary>
/// Territory -> ContentFinderCondition, built once. Answers both "what is this duty called" and
/// "am I even in a duty right now".
/// </summary>
/// <remarks>
/// Separate from <see cref="DutyCatalog"/> because that only covers duties we have loot for, and is
/// rebuilt whenever the dataset changes. This covers every duty in the game and never changes.
/// </remarks>
public sealed class ContentFinderIndex
{
    private readonly Dictionary<uint, ContentFinderCondition> byTerritory;

    private ContentFinderIndex(Dictionary<uint, ContentFinderCondition> byTerritory) =>
        this.byTerritory = byTerritory;

    private const uint DungeonContentType = 2;
    private const uint RaidContentType = 5;

    /// <summary>ContentMemberType 4 is the 24-player alliance layout; 8-player raids use 3.</summary>
    private const uint AllianceMemberType = 4;

    public bool TryGet(uint territoryId, out ContentFinderCondition condition) =>
        byTerritory.TryGetValue(territoryId, out condition);

    /// <summary>True when the territory is an instanced duty rather than an open-world zone.</summary>
    public bool IsDuty(uint territoryId) => byTerritory.ContainsKey(territoryId);

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

    public static bool IsSupported(ContentFinderCondition condition) =>
        condition.ContentType.RowId == DungeonContentType ||
        (condition.ContentType.RowId == RaidContentType &&
         condition.ContentMemberType.RowId == AllianceMemberType);

    public static ContentFinderIndex Build()
    {
        var byTerritory = new Dictionary<uint, ContentFinderCondition>();

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
        }

        return new ContentFinderIndex(byTerritory);
    }
}
