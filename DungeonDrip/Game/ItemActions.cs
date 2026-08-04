using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling;

namespace DungeonDrip.Game;

/// <summary>Which right-click menu is being built, for the entries that differ between them.</summary>
public enum ItemActionSurface
{
    /// <summary>One of the plugin's own windows, which offers everything.</summary>
    PluginWindow,

    /// <summary>
    /// The game's own menu, where anything the game already offers has to be left out.
    /// </summary>
    GameMenu,
}

/// <summary>One thing the user can choose to do with a piece of gear.</summary>
/// <param name="Submenu">
/// Present when this is a heading rather than a choice. A heading's own <see cref="Invoke"/> is
/// never called.
/// </param>
/// <param name="StartsGroup">
/// Whether a divider belongs above this entry. Decided here rather than by the renderer, so the
/// grouping stays with the list that knows why the groups exist.
/// </param>
public sealed record ItemAction(
    string Label,
    Action? Invoke = null,
    IReadOnlyList<ItemAction>? Submenu = null,
    bool StartsGroup = false)
{
    public bool IsSubmenu => Submenu is { Count: > 0 };
}

/// <summary>
/// Everything the plugin offers for a piece of gear, in one list rather than one per menu.
/// </summary>
/// <remarks>
/// These get drawn in two places - the plugin's own windows and the game's right-click menu - which
/// render completely differently. Keeping two hand-written copies of the list would mean the next
/// action added lands in only one of them, so the list is built once here and each menu is a
/// renderer over it.
/// </remarks>
public static class ItemActions
{
    /// <summary>How many duties to name before the rest collapse into a count.</summary>
    private const int MaxNamedSources = 8;

    public static IReadOnlyList<ItemAction> For(
        Plugin plugin, uint itemId, string name, ItemActionSurface surface)
    {
        var actions = new List<ItemAction>();

        // Trying on is the one thing here that reaches into the game, so it is kept off the pieces
        // the game will not preview: an entry that silently does nothing is worse than no entry.
        if (TryOnService.CanTryOn(itemId))
        {
            // The game already offers Try On on its own gear menus, so a second one there would sit
            // next to the real thing saying the same word. Whole outfits have no native equivalent.
            if (surface == ItemActionSurface.PluginWindow)
                actions.Add(new ItemAction("Try on", () => plugin.TryOn.QueuePiece(itemId)));

            // Named rather than a bare "Try on outfit", because a piece is often in several sets and
            // the whole point of the entry is choosing which of them to look at.
            foreach (var (setId, setName) in plugin.Outfits.NamedSetsContaining(itemId))
                actions.Add(new ItemAction($"Try on outfit: {setName}", () => plugin.TryOn.QueueOutfit(setId)));
        }

        // Only on the game's menus. Inside the plugin's own windows the answer is already on
        // screen: the duty list is a duty, the loot companion is the roll in progress, the vendor
        // panel is stock rather than drops, and the collection view prints the source under each
        // missing piece.
        if (surface == ItemActionSurface.GameMenu)
        {
            var drops = DropSubmenu(plugin, itemId);
            if (drops != null)
                actions.Add(drops with { StartsGroup = actions.Count > 0 });
        }

        // Both of these the game offers already on its own menus. Divided off from the entries
        // above, which are the only ones that act on the game rather than on a window.
        if (surface == ItemActionSurface.PluginWindow)
        {
            actions.Add(new ItemAction(
                "Link in chat",
                () => Plugin.ChatGui.Print(new SeStringBuilder().AddItemLink(itemId, false).Build()),
                StartsGroup: actions.Count > 0));

            actions.Add(new ItemAction("Copy name", () => ImGui.SetClipboardText(name)));
        }

        return actions;
    }

    /// <summary>
    /// The duties a piece drops in, as a submenu that navigates to whichever one is chosen.
    /// </summary>
    /// <remarks>
    /// An empty answer still gets the entry, carrying the reason. Loot coverage is thin for new
    /// content by design - the wiki is only read for duties that have been opened - so "nothing lists
    /// this" and "this does not drop" are different statements and only the first can honestly be
    /// made here. Dropping the entry when there is no answer would read as the menu being broken,
    /// which is why it is a disabled line rather than an absence.
    /// </remarks>
    private static ItemAction? DropSubmenu(Plugin plugin, uint itemId)
    {
        var drops = plugin.Drops;

        // No loot data at all yet - a different situation from having looked and found nothing,
        // and not one this menu should editorialise about.
        if (drops == null)
            return null;

        // Only worth answering for something that could have been a drop in the first place.
        if (!plugin.Storage.CanBeStored(itemId))
            return null;

        var sources = drops.For(itemId);
        var entries = new List<ItemAction>(Math.Min(sources.Count, MaxNamedSources) + 2);

        if (sources.Count == 0)
        {
            entries.Add(new ItemAction("Nothing in the loot data lists this piece"));
        }
        else
        {
            for (var i = 0; i < sources.Count; i++)
            {
                if (i == MaxNamedSources)
                {
                    // Hands the rest to the picker rather than growing the menu without limit.
                    var name = plugin.Duties?.NameOf(sources[i].TerritoryId) ?? string.Empty;
                    entries.Add(new ItemAction(
                        $"...and {sources.Count - i} more", () => plugin.ShowDutyPicker(name)));
                    break;
                }

                var source = sources[i];
                var label = source.Level > 0
                    ? $"{source.DutyName}  (Lv. {source.Level})"
                    : source.DutyName;

                entries.Add(new ItemAction(label, () => plugin.ShowDuty(source.TerritoryId)));
            }
        }

        return new ItemAction("Where does this drop?", Submenu: entries);
    }
}
