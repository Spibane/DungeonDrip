using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Utility;

namespace DungeonDrip.Game;

/// <summary>
/// Puts the plugin's gear actions on the game's own right-click menu.
/// </summary>
/// <remarks>
/// A second renderer over <see cref="ItemActions"/>, so what appears here cannot drift from what
/// the plugin's own windows offer.
///
/// This adds to game UI, but not the way the tooltip line does: Dalamud's context menu API exists
/// precisely so plugins can contribute entries, and it composes them rather than letting anyone
/// overwrite anyone else. Nothing here touches an addon's nodes.
///
/// Everything runs on the game's main thread, not the draw thread, so nothing here may touch state
/// the panels own. The ownership resolver and the drop index are both read-only and cheap, and this
/// fires once per menu rather than once per frame, so there is nothing worth caching anyway.
/// </remarks>
public sealed class GameContextMenu : IDisposable
{
    /// <summary>
    /// Default menus we will read the hovered item from.
    /// </summary>
    /// <remarks>
    /// An allow-list rather than "any default menu", because a default menu is usually about a
    /// person. Right-clicking a player with a gear tooltip still up would otherwise hang try-on
    /// entries off them.
    /// </remarks>
    private static readonly HashSet<string> HoverAddons =
    [
        "CharacterInspect",
        "ChatLog",
        "ContentsInfoDetail",
        "MiragePrismPrismBox",
        "MiragePrismRemove",
        "ItemSearch",
        "ItemSearchResult",
        "RecipeNote",
        "RecipeTree",
        "Shop",
        "ShopExchangeItem",
        "ShopExchangeCurrency",
        "InclusionShop",
        "FreeShop",
    ];

    /// <summary>
    /// Below the game's own entries. Displacing Discard or Try On would be presumptuous, and worse,
    /// would move the row someone's muscle memory is aimed at.
    /// </summary>
    private const int Priority = 0;

    private readonly Plugin plugin;

    public GameContextMenu(Plugin plugin)
    {
        this.plugin = plugin;
        Plugin.ContextMenu.OnMenuOpened += OnMenuOpened;
    }

    public void Dispose() => Plugin.ContextMenu.OnMenuOpened -= OnMenuOpened;

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (!plugin.Configuration.ShowGameContextMenu)
            return;

        var itemId = TargetItemId(args);
        if (itemId == 0)
            return;

        // Same rule as every other surface: if it cannot be kept as a glamour, the plugin has
        // nothing to say, and saying nothing keeps an absent entry meaningful.
        if (!plugin.Storage.CanBeStored(itemId))
            return;

        var name = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>()
            .TryGetRow(itemId, out var item) ? item.Name.ExtractText() : string.Empty;

        var actions = ItemActions.For(plugin, itemId, name, ItemActionSurface.GameMenu);
        if (actions.Count == 0)
            return;

        // One entry when there is one thing to offer; a single named submenu when there are
        // several, so a piece belonging to four outfit sets does not add five rows to every
        // right-click in the game.
        args.AddMenuItem(actions.Count == 1
            ? Build(actions[0])
            : new MenuItem
            {
                Name = "Dungeon Drip",
                PrefixChar = 'D',
                PrefixColor = 541,
                Priority = Priority,
                IsSubmenu = true,
                OnClicked = clicked => clicked.OpenSubmenu("Dungeon Drip", [.. actions.Select(Build)]),
            });
    }

    private static MenuItem Build(ItemAction action) => new()
    {
        Name = action.Label,
        PrefixChar = 'D',
        PrefixColor = 541,
        Priority = Priority,
        IsSubmenu = action.IsSubmenu,

        // A label with nothing behind it - the drop list explains itself with those. Greyed out
        // rather than dropped, because a menu that silently omits its own explanation reads worse
        // than one that shows it unclickable.
        IsEnabled = action.IsSubmenu || action.Invoke != null,
        OnClicked = Handler(action),
    };

    private static Action<IMenuItemClickedArgs>? Handler(ItemAction action)
    {
        if (action.IsSubmenu)
            return clicked => clicked.OpenSubmenu(action.Label, [.. action.Submenu!.Select(Build)]);

        return action.Invoke == null ? null : _ => action.Invoke();
    }

    /// <summary>
    /// The gear this menu is about, or zero when it is not about gear.
    /// </summary>
    /// <remarks>
    /// Two routes, because the game has two kinds of menu. An inventory menu carries the item
    /// outright, which is the reliable case and covers bags, the armoury and the saddlebag. A
    /// default menu carries a person, so for the windows that show gear without it being in an
    /// inventory - the inspect window, a chat link, the dresser - the id has to come from what is
    /// hovered, which is why that path is held to a list of addons known to be showing gear.
    /// </remarks>
    private static uint TargetItemId(IMenuArgs args)
    {
        var raw = args.Target is MenuTargetInventory { TargetItem: { } target }
            ? target.ItemId
            : args.MenuType == ContextMenuType.Default && HoverAddons.Contains(args.AddonName ?? string.Empty)
                ? HoveredItemId()
                : 0u;

        if (raw == 0)
            return 0;

        var (itemId, kind) = ItemUtil.GetBaseId(raw);

        // Key items sit behind an offset that shares the id space with real gear, so stripping it
        // blindly maps them onto unrelated equipment. Collectables are a turn-in, not a glamour
        // decision. HQ is not excluded - HQ gear is real gear, already collapsed onto the base id.
        return kind is ItemKind.EventItem or ItemKind.Collectible ? 0u : itemId;
    }

    /// <summary>
    /// The item the cursor is over, as the game reports it.
    /// </summary>
    /// <remarks>
    /// Shared with the tooltip line, which needs exactly the same answer to exactly the same
    /// question, and both of them are wrong in the same way if this ever lags a hover.
    /// </remarks>
    public static uint HoveredItemId()
    {
        var hovered = Plugin.GameGui.HoveredItem;
        return hovered is > 0 and <= uint.MaxValue ? (uint)hovered : 0u;
    }
}
