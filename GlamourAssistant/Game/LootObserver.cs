using System;
using System.Linq;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using GlamourAssistant.Core;
using GlamourAssistant.Data;
using Lumina.Excel.Sheets;

namespace GlamourAssistant.Game;

/// <summary>
/// Watches loot messages and records what actually dropped, so the plugin's picture of a duty
/// improves by playing it even when the upstream dataset has nothing.
/// </summary>
/// <remarks>
/// Chat is used rather than the Need/Greed addon because loot chat carries an item link, so the
/// item id comes straight out of the payload with no text parsing and no localisation problem - and
/// it covers rolls won by other party members, not just your own.
/// </remarks>
public sealed class LootObserver : IDisposable
{
    private readonly Configuration configuration;
    private readonly LearnedLootStore store;
    private readonly ContentFinderIndex duties;

    public LootObserver(Configuration configuration, LearnedLootStore store, ContentFinderIndex duties)
    {
        this.configuration = configuration;
        this.store = store;
        this.duties = duties;

        Plugin.ChatGui.ChatMessage += OnChatMessage;
    }

    public void Dispose() => Plugin.ChatGui.ChatMessage -= OnChatMessage;

    private void OnChatMessage(IHandleableChatMessage message)
    {
        if (!configuration.LearnDropsFromLoot)
            return;

        // Loot-specific kinds only. A linked item in party chat is not evidence of a drop.
        if (message.LogKind is not (XivChatType.LootNotice or XivChatType.LootRoll))
            return;

        var territoryId = Plugin.ClientState.TerritoryType;
        if (territoryId == 0 || !duties.IsDuty(territoryId))
            return;

        var items = Plugin.DataManager.GetExcelSheet<Item>();

        foreach (var payload in message.Message.Payloads.OfType<ItemPayload>())
        {
            var itemId = ItemId.Normalize(payload.ItemId);

            if (!IsGlamourableGear(items, itemId))
                continue;

            if (store.Add(territoryId, itemId))
            {
                Plugin.Log.Information(
                    $"Learned drop: {items.GetRow(itemId).Name.ExtractText()} ({itemId}) in territory {territoryId}");
            }
        }
    }

    /// <summary>Same test the bundled dataset is filtered by, so learned entries stay consistent.</summary>
    private static bool IsGlamourableGear(Lumina.Excel.ExcelSheet<Item> items, uint itemId)
    {
        if (!items.TryGetRow(itemId, out var item))
            return false;

        if (item.EquipSlotCategory.RowId == 0 || !item.EquipSlotCategory.IsValid)
            return false;

        return item.EquipSlotCategory.Value.SoulCrystal == 0;
    }
}
