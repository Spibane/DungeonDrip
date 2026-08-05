using Lumina.Excel.Sheets;

namespace DungeonDrip.Core.Sources;

/// <summary>
/// Gear handed out for an achievement, named by the achievement that gives it.
/// </summary>
/// <remarks>
/// Deliberately does not say whether the achievement is already earned. That would need the
/// achievement's own completion state, which the client only holds after the Achievements window has
/// been opened - so the honest answers would be "earned", "not earned" and "cannot say yet", and the
/// third would be the one shown most often. Naming the achievement lets the player check in a window
/// that always knows.
/// </remarks>
internal static class AchievementSources
{
    public static void Contribute(ItemSources.Accumulator into)
    {
        foreach (var achievement in Plugin.DataManager.GetExcelSheet<Achievement>())
        {
            var itemId = achievement.Item.RowId;
            if (itemId == 0 || !into.Wanted(itemId))
                continue;

            var name = achievement.Name.ExtractText();
            into.Add(itemId, new AcquisitionSource(
                AcquisitionKind.Achievement, "Achievement", name.Length > 0 ? name : null));
        }
    }
}
