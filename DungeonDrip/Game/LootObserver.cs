using System;
using System.Linq;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Utility;
using DungeonDrip.Core;
using DungeonDrip.Data;
using Lumina.Excel.Sheets;

namespace DungeonDrip.Game;

/// <summary>
/// Watches loot messages and records what actually dropped, so the plugin's picture of a duty
/// improves by playing it even when the upstream dataset has nothing.
/// </summary>
/// <remarks>
/// Chat is used rather than the Need/Greed addon because loot chat carries an item link, so the
/// item id comes straight out of the payload with no text parsing and no localisation problem - and
/// it covers rolls won by other party members, not just the player's own.
/// </remarks>
public sealed class LootObserver : IDisposable
{
    private readonly Configuration configuration;
    private readonly LearnedLootStore store;
    private readonly ContentFinderIndex duties;
    private readonly StorageEligibility storage;

    public LootObserver(
        Configuration configuration,
        LearnedLootStore store,
        ContentFinderIndex duties,
        StorageEligibility storage)
    {
        this.configuration = configuration;
        this.store = store;
        this.duties = duties;
        this.storage = storage;

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
        if (territoryId == 0 || !duties.IsSupportedDuty(territoryId))
            return;

        var items = Plugin.DataManager.GetExcelSheet<Item>();

        foreach (var payload in message.Message.Payloads.OfType<ItemPayload>())
        {
            // ItemPayload.ItemId is already the base id; only the kind still needs excluding.
            if (payload.Kind == ItemKind.EventItem)
                continue;

            var itemId = payload.ItemId;
            if (!storage.CanBeStored(itemId))
                continue;

            if (store.Add(territoryId, itemId))
            {
                Plugin.Log.Information(
                    $"Learned drop: {items.GetRow(itemId).Name.ExtractText()} ({itemId}) in territory {territoryId}");
            }
        }
    }

}
