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
    private readonly Configuration configuration;
    private readonly Queue<uint> pending = new();

    /// <summary>Whether what is queued is meant to be worn all at once.</summary>
    private bool wearTogether;

    /// <summary>Whether the fitting room should be emptied before the queue is fed to it.</summary>
    private bool clearFirst;

    public TryOnService(OutfitCatalog outfits, Configuration configuration)
    {
        this.outfits = outfits;
        this.configuration = configuration;
    }

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

        if (pending.Count == before)
            return;

        wearTogether = true;
        clearFirst |= configuration.ClearFittingRoomForOutfits;
    }

    public void Clear()
    {
        pending.Clear();
        wearTogether = false;
        clearFirst = false;
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

        // Spends this tick on the close and nothing else. The room takes a frame to go away, and a
        // piece handed over in the same one lands in the fitting room that is on its way out.
        if (clearFirst)
        {
            clearFirst = false;

            if (Empty())
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

    /// <summary>
    /// Throws away whatever is in the fitting room. Says whether there was anything to throw.
    /// </summary>
    /// <remarks>
    /// The agent's list is emptied in place rather than the room being closed. Closing only puts the
    /// window away - the agent keeps its items, so the next piece reopens the room with everything
    /// still in it, which is the whole thing this is meant to prevent.
    ///
    /// The list and the figure wearing it are separate: clearing the array empties the list the room
    /// shows, and the preview character goes on wearing all of it until told otherwise. Only the
    /// preview is discarded either way - outfits saved out of the room live in the Glamour Dresser
    /// and are not touched.
    /// </remarks>
    private static bool Empty()
    {
        var agent = AgentTryon.Instance();
        if (agent == null)
            return false;

        var items = agent->TryOnItems;
        var cleared = false;

        for (var i = 0; i < items.Length; i++)
        {
            if (items[i].Id == 0)
                continue;

            items[i] = default;
            cleared = true;
        }

        if (!cleared)
            return false;

        agent->TryOnItemsChanged = true;
        Plugin.Log.Debug("Emptied the fitting room before an outfit.");

        // Guarded on the view's own flags, which are what the game passes here: stripping a figure
        // whose character data has not arrived is asking it to undress nothing.
        if (agent->CharaView.CharacterLoaded)
            agent->CharaView.UnequipGear(agent->CharaView.CharacterDataCopied, true);

        return true;
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
