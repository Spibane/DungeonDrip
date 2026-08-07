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
/// A node has no right-click event to subscribe to. The click arrives as
/// <see cref="AtkEventType.MouseClick"/> and which button it was has to be read out of the event
/// data, which is why this is the raw handler rather than the tidy <c>Action</c> overload.
/// </remarks>
internal static class NativeContextMenu
{
    /// <summary>The mouse button id the game reports for a right-click.</summary>
    private const int RightButton = 1;

    public static unsafe void Attach(NodeBase node, Plugin plugin, uint itemId, string name)
    {
        node.AddEvent(AtkEventType.MouseClick, (AtkEventListener.Delegates.ReceiveEvent)Handle);

        void Handle(
            AtkEventListener* self, AtkEventType type, int param, AtkEvent* atkEvent,
            AtkEventData* data)
        {
            if (data is null || data->MouseData.ButtonId != RightButton)
                return;

            var menu = new Menu();

            foreach (var action in ItemActions.For(plugin, itemId, name, ItemActionSurface.PluginWindow))
            {
                // Submenus and labels are dropped rather than flattened. A label is a line the drop
                // list uses to explain itself and reads as a broken entry when it is clickable, and
                // one level of submenu is all the game's menu supports - so anything deeper would
                // have to be flattened into names that no longer say what they belong to.
                if (action.Invoke == null || action.IsSubmenu)
                    continue;

                menu.AddItem(action.Label, action.Invoke);
            }

            menu.Open();
        }
    }
}
