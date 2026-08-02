using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;

namespace DungeonDrip.Game;

/// <summary>
/// Sends gear to the game's Fitting Room, one piece per frame.
/// </summary>
/// <remarks>
/// The only place the plugin asks the game to do something rather than reading what it has already
/// done, and only ever off the back of a click. The queue exists because the agent takes one item per
/// call and drops the rest if they arrive in the same frame, so a whole outfit has to be fed in over
/// several - eleven frames at worst, which nobody sees.
/// </remarks>
public sealed unsafe class TryOnService
{
    /// <summary>
    /// Well past the eleven slots of the largest outfit, so a queued set always survives, and far
    /// short of what a stuck finger on the mouse could pile up.
    /// </summary>
    private const int Capacity = 32;

    private readonly OutfitCatalog outfits;
    private readonly Queue<uint> pending = new();

    /// <summary>Whether what is queued is meant to be worn all at once.</summary>
    private bool wearTogether;

    public TryOnService(OutfitCatalog outfits) => this.outfits = outfits;

    /// <summary>Preview a single piece.</summary>
    /// <remarks>
    /// Leaves the fitting room's outfit mode alone. One piece is the case that mode exists to be
    /// turned off for, so a user who has it off asked for exactly this.
    /// </remarks>
    public void QueuePiece(uint itemId) => Enqueue(itemId);

    /// <summary>Preview every piece of an outfit set, weapon first and then top down.</summary>
    public void QueueOutfit(uint setId)
    {
        var before = pending.Count;

        foreach (var piece in outfits.PiecesInSlotOrder(setId))
            Enqueue(piece);

        if (pending.Count > before)
            wearTogether = true;
    }

    public void Clear()
    {
        pending.Clear();
        wearTogether = false;
    }

    /// <summary>
    /// Hands the next queued piece to the fitting room. Called once per framework tick.
    /// </summary>
    /// <remarks>
    /// A refusal is logged and the queue keeps draining rather than being dropped. The game says no
    /// while you are in combat, a cutscene or GPose, and each piece is asked separately anyway, so
    /// letting the rest through costs a handful of no-ops in the case where nothing was going to work
    /// - against losing the other ten pieces of an outfit to one unreadable return value.
    /// Nothing is ever retried: a queue that waited out a cutscene would spring an outfit on you
    /// minutes after you asked for it.
    /// </remarks>
    public void Tick()
    {
        if (pending.Count == 0)
            return;

        if (!Plugin.ClientState.IsLoggedIn)
        {
            Clear();
            return;
        }

        if (wearTogether)
            WearTogether();

        var itemId = pending.Dequeue();

        // stain0, stain1 and the glamour override stay 0: this previews the piece as it comes, and a
        // dye the user has not chosen would be a guess presented as their gear.
        if (!AgentTryon.TryOn(0, itemId, 0, 0, 0, false))
            Plugin.Log.Debug($"Fitting room declined item {itemId}.");

        if (pending.Count == 0)
            wearTogether = false;
    }

    /// <summary>
    /// Puts the fitting room in outfit mode, where pieces accumulate instead of replacing each other.
    /// </summary>
    /// <remarks>
    /// With the room's Save/Delete Outfit option off, every try-on overwrites the last, so an outfit
    /// fed in a piece at a time shows only whichever piece arrived last. The flag is set every tick
    /// rather than once up front: the first piece is what opens the room, and the agent may not exist
    /// to be told anything until it has.
    ///
    /// Left on afterwards rather than restored. Turning it back off is what discards the outfit that
    /// was just assembled, and it is a visible checkbox the user can flip themselves.
    /// </remarks>
    private static void WearTogether()
    {
        var agent = AgentTryon.Instance();
        if (agent != null)
            agent->SaveDeleteOutfit = true;
    }

    /// <summary>Whether the fitting room can show this at all - it only takes equipment.</summary>
    public static bool CanTryOn(uint itemId) =>
        Plugin.DataManager.GetExcelSheet<Item>().TryGetRow(itemId, out var item)
        && item.EquipSlotCategory.RowId != 0;

    private void Enqueue(uint itemId)
    {
        if (itemId == 0 || pending.Count >= Capacity || !CanTryOn(itemId))
            return;

        pending.Enqueue(itemId);
    }
}
