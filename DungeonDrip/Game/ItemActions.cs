using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling;
using DungeonDrip.Core.Sources;

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
    /// <remarks>
    /// Only the duties are capped. The non-duty routes below them are already one line per kind, which
    /// caps them at three in practice - see <see cref="Core.Sources.ItemSources.Accumulator.Finish"/>.
    /// </remarks>
    private const int MaxNamedDuties = 8;

    /// <summary>
    /// The actions for one piece on one surface, in the order they should be drawn.
    /// </summary>
    /// <remarks>
    /// The surface decides what is left out rather than what is added, and the rule is the same in
    /// both directions: never offer what the surface already provides. The game's own menus have Try
    /// On and item links, so those are dropped there; the plugin's windows already show where a piece
    /// drops, so the drop submenu is dropped here.
    ///
    /// An empty list is a real answer - a piece the game will not preview, on a menu with nothing else
    /// to offer - and callers add no entries at all rather than an empty heading.
    /// </remarks>
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
            var drops = SourceSubmenu(plugin, itemId);
            if (drops != null)
                actions.Add(drops with { StartsGroup = actions.Count > 0 });
        }

        // On both surfaces, unlike everything else here. The window rows show where a piece comes
        // from but cannot be clicked through to a reference site, and the game's menus offer no such
        // link at all, so neither surface already provides this.
        if (plugin.Storage.CanBeStored(itemId))
        {
            var site = plugin.Configuration.LookupSite;
            actions.Add(new ItemAction(
                ItemLink.NameOf(site),
                () => Dalamud.Utility.Util.OpenLink(ItemLink.For(site, itemId, name)),
                StartsGroup: actions.Count > 0));
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
    /// Every route to a piece, as a submenu: duties first, which are the only navigable ones.
    /// </summary>
    /// <remarks>
    /// Duties lead because they are the only entries that can be acted on - choosing one pins it and
    /// opens the window. The crafted and bought routes below them carry no destination and are drawn
    /// as inert lines, an <see cref="ItemAction"/> with no <see cref="ItemAction.Invoke"/>, exactly as
    /// the "nothing lists this" line already was.
    ///
    /// An empty answer still gets the entry, carrying the reason, and the reason has to be careful in
    /// both directions. Loot coverage is thin for new content by design - the wiki is only read for
    /// duties that have been opened - and the sheets, while exact about recipes and shops, know
    /// nothing of the Mog Station, seasonal events, deep dungeons or relic steps. So "nothing lists
    /// this" is sayable and "this cannot be obtained" is not. Dropping the entry when there is no
    /// answer would read as the menu being broken, which is why it is an inert line rather than an
    /// absence.
    /// </remarks>
    private static ItemAction? SourceSubmenu(Plugin plugin, uint itemId)
    {
        // Only worth answering for something that could have been a drop in the first place.
        if (!plugin.Storage.CanBeStored(itemId))
            return null;

        var drops = plugin.Drops;

        // Read once, since the property builds the index on first touch. Null means nothing looked -
        // the setting is off - which has to stay distinct from "looked and found nothing" below.
        var index = plugin.ItemSources;
        var acquisitions = index?.For(itemId) ?? [];

        // Nothing consulted at all yet: no loot data, and no source index either. A different situation
        // from having looked and found nothing, and not one this menu should editorialise about.
        if (drops == null && index == null)
            return null;

        var duties = drops?.For(itemId) ?? [];
        var entries = new List<ItemAction>(
            Math.Min(duties.Count, MaxNamedDuties) + acquisitions.Count + 2);

        for (var i = 0; i < duties.Count; i++)
        {
            if (i == MaxNamedDuties)
            {
                // Hands the rest to the picker rather than growing the menu without limit.
                var dutyName = plugin.Duties?.NameOf(duties[i].TerritoryId) ?? string.Empty;
                entries.Add(new ItemAction(
                    $"...and {duties.Count - i} more", () => plugin.ShowDutyPicker(dutyName)));
                break;
            }

            var duty = duties[i];
            var label = duty.Level > 0 ? $"{duty.DutyName}  (Lv. {duty.Level})" : duty.DutyName;
            entries.Add(new ItemAction(label, () => plugin.ShowDuty(duty.TerritoryId)));
        }

        // Divided off from the duties above, which are the clickable ones. Uncapped, because one line
        // per kind holds it to three in practice - see ItemSources.Accumulator.Finish.
        var first = true;
        foreach (var acquisition in acquisitions)
        {
            entries.Add(new ItemAction(
                acquisition.Describe(), StartsGroup: first && entries.Count > 0));
            first = false;
        }

        // Only where something looked. With the source index off, the duties are all that was consulted
        // and the wording has to say so rather than implying the sheets were read too.
        if (entries.Count == 0)
        {
            entries.Add(new ItemAction(index == null
                ? "Nothing in the loot data lists this piece"
                : "Source unknown"));
        }

        return new ItemAction("Where does this come from?", Submenu: entries);
    }
}
