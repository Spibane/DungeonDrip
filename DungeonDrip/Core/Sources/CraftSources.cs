using Lumina.Excel.Sheets;

namespace DungeonDrip.Core.Sources;

/// <summary>
/// The recipe sheet read backwards: which job makes a given piece, and at what level.
/// </summary>
/// <remarks>
/// The one source here that is complete without caveat. Every craftable item in the game has a
/// <c>Recipe</c> row, so a piece absent from this sweep genuinely cannot be crafted - which is not
/// something any other builder in this folder can claim about its own sheet.
///
/// Says nothing about materials. "Crafted" plus the job and level is the whole of what a collector
/// needs to know to decide whether to pursue a piece; the ingredient tree is what the linked reference
/// site is for, and duplicating it here would be a worse copy of a solved problem.
/// </remarks>
internal static class CraftSources
{
    public static void Contribute(ItemSources.Accumulator into)
    {
        foreach (var recipe in Plugin.DataManager.GetExcelSheet<Recipe>())
        {
            var itemId = recipe.ItemResult.RowId;
            if (itemId == 0 || !into.Wanted(itemId))
                continue;

            var job = recipe.CraftType.IsValid
                ? recipe.CraftType.Value.Name.ExtractText()
                : string.Empty;

            // The recipe's own level, not the item's equip level - they diverge for anything
            // upgradeable, and the useful number is the one that decides whether it can be made.
            var level = recipe.RecipeLevelTable.IsValid
                ? recipe.RecipeLevelTable.Value.ClassJobLevel
                : (byte)0;

            var detail = (job.Length > 0, level > 0) switch
            {
                (true, true) => $"{job} Lv. {level}",
                (true, false) => job,
                _ => null,
            };

            into.Add(itemId, new AcquisitionSource(AcquisitionKind.Crafted, "Crafted", detail));
        }
    }
}
