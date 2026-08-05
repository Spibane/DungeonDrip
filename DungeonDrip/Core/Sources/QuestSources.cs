using Lumina.Excel.Sheets;

namespace DungeonDrip.Core.Sources;

/// <summary>
/// Gear given for finishing a quest, named by the quest and its level.
/// </summary>
/// <remarks>
/// The least trustworthy sheet in this folder, and the reason to keep it separate from the shop and
/// recipe sweeps rather than folding all the reward-shaped sources together. <c>Quest</c> stores its
/// rewards in fixed-width columns whose meaning depends on the quest's own type, so a row ref that
/// looks like an item can be a currency, an action or a crafting recipe. Every candidate is checked
/// against the <c>Item</c> sheet and the storage test before it is believed.
///
/// <para><b>A named quest may be one already completed, and this cannot tell.</b> Completion state is
/// per-character client data, not sheet data, so the quest is named and the player is left to
/// recognise it. That is also why the quest's level is included - it is what makes an old quest
/// recognisable as one already done.</para>
///
/// Optional rewards are reported the same as guaranteed ones. Both are routes to the piece; the
/// difference is that an optional reward is a choice between several, which the quest's own window
/// shows properly.
/// </remarks>
internal static class QuestSources
{
    public static void Contribute(ItemSources.Accumulator into)
    {
        foreach (var quest in Plugin.DataManager.GetExcelSheet<Quest>())
        {
            var name = quest.Name.ExtractText();
            if (name.Length == 0)
                continue;

            var detail = quest.ClassJobLevel is [var level, ..] && level > 0
                ? $"{name} (Lv. {level})"
                : name;

            foreach (var reward in quest.Reward)
                Offer(into, reward.RowId, detail);

            foreach (var optional in quest.OptionalItemReward)
                Offer(into, optional.RowId, detail);
        }
    }

    /// <summary>
    /// Records a candidate reward, if it turns out to be a storable piece of gear at all.
    /// </summary>
    /// <remarks>
    /// The row id arrives untyped for the reason in the type's remarks, so the <c>Wanted</c> test is
    /// doing double duty here: it rejects the materials and consumables as it does everywhere else, and
    /// it also rejects the ids that were never item ids to begin with. An id that happens to collide
    /// with a real equippable item would slip through, which is the standing risk of reading these
    /// columns and is why nothing else in this folder is shaped this way.
    /// </remarks>
    private static void Offer(ItemSources.Accumulator into, uint itemId, string detail)
    {
        if (itemId == 0 || !into.Wanted(itemId))
            return;

        into.Add(itemId, new AcquisitionSource(AcquisitionKind.Quest, "Quest", detail));
    }
}
