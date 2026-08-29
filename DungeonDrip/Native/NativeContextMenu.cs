using System;
using DungeonDrip.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using Menu = KamiToolKit.ContextMenu.ContextMenu;

namespace DungeonDrip.Native;

/// <summary>Puts a gear row's right-click menu on a native node.</summary>
/// <remarks>
/// The third rendering of one list of actions, after the ImGui popup and the entries the plugin
/// adds to the game's own menus - and like those, it builds from <see cref="ItemActions"/> rather
/// than naming any action itself. A second hand-written copy is how an action ends up on one
/// surface and not the others, which is the whole reason that list exists.
///
/// <b>One menu, owned for the life of the panel and refilled per click.</b> Not one per row, and
/// emphatically not one per right-click: opening a menu hands the game a pointer to an unmanaged
/// callback shim the menu owns, and the game holds that pointer for as long as the menu is up. A
/// menu built as a local and dropped is collected while the game is still pointing at it, and
/// choosing an entry then takes the client down - which is what trying an outfit on used to do.
///
/// A node has no right-click event to subscribe to. The click arrives as
/// <see cref="AtkEventType.MouseClick"/> and which button it was has to be read out of the event
/// data, which is why this is the raw handler rather than the tidy <c>Action</c> overload.
/// </remarks>
internal sealed class NativeContextMenu(Plugin plugin) : IDisposable
{
    /// <summary>The mouse button id the game reports for a right-click.</summary>
    private const int RightButton = 1;

    private readonly Menu menu = new();

    /// <summary>Hangs this menu on a row, for the piece that row is about.</summary>
    public unsafe void Attach(NodeBase node, uint itemId, string name)
    {
        node.AddEvent(AtkEventType.MouseClick, (AtkEventListener.Delegates.ReceiveEvent)Handle);

        void Handle(
            AtkEventListener* self, AtkEventType type, int param, AtkEvent* atkEvent,
            AtkEventData* data)
        {
            if (data is null || data->MouseData.ButtonId != RightButton)
                return;

            Show(itemId, name);
        }
    }

    private void Show(uint itemId, string name)
    {
        menu.Clear();

        foreach (var action in ItemActions.For(plugin, itemId, name, ItemActionSurface.PluginWindow))
        {
            // Submenus and labels are dropped rather than flattened. A label is a line the drop
            // list uses to explain itself and reads as a broken entry when it is clickable, and
            // one level of submenu is all the game's menu supports - so anything deeper would have
            // to be flattened into names that no longer say what they belong to.
            if (action.Invoke == null || action.IsSubmenu)
                continue;

            menu.AddItem(action.Label, action.Invoke);
        }

        menu.Open();
    }

    public void Dispose() => menu.Dispose();
}
