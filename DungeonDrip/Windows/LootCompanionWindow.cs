using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using DungeonDrip.Game;
using DungeonDrip.Presentation;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace DungeonDrip.Windows;

/// <summary>
/// A small window that rides alongside the Need/Greed roll window, marking which of the items up
/// for roll are not collected yet.
/// </summary>
/// <remarks>
/// Deliberately a separate window rather than recolouring the loot addon's own nodes. Several
/// popular plugins (Allagan Tools, Simple Tweaks, VanillaPlus, Collections) already write to game
/// addon nodes, and two plugins fighting over the same node's colour is a real conflict - whoever
/// writes last in a frame wins, and a refresh can revert either. This reads the addon's item list
/// and screen position and draws nothing into it, so by construction there is nothing to collide
/// with. It also survives the addon's node layout changing in a patch.
///
/// Auto-resizing and no toolbar, unlike the panels, and neither is an oversight: a roll is a
/// thirty-second decision, so a window that changes size mid-roll or asks to be configured during
/// one is hostile. What it lists comes from <see cref="LootRollWatcher"/>, which is stable for the
/// length of a roll for the same reason.
/// </remarks>
public sealed unsafe class LootCompanionWindow : Window
{
    private readonly Plugin plugin;

    /// <summary>
    /// This window's own width, measured at the end of the last frame. PreDraw runs before its
    /// Begin, so ImGui's current-window queries cannot be asked there.
    /// </summary>
    private float lastWidth = 220f;

    // Collapsible because it cannot be moved: folding it is the only way to get it out of the way.
    public LootCompanionWindow(Plugin plugin)
        : base("Still needed###DungeonDripLootCompanion",
               ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
               ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing |
               ImGuiWindowFlags.NoNav)
    {
        this.plugin = plugin;
        RespectCloseHotkey = false;
        ShowCloseButton = false;
        DisableWindowSounds = true;
    }

    /// <summary>Only draws while the roll window is actually up and holding items.</summary>
    public override bool DrawConditions() => plugin.Loot.Resolve() != null;

    /// <summary>Parks the window against whichever side of the roll window has room for it.</summary>
    public override void PreDraw()
    {
        var unit = Plugin.GameGui.GetAddonByName<AtkUnitBase>(LootRollWatcher.AddonName);
        if (unit == null)
            return;

        // Auto-sized, so its own height is not knowable here; the roll window's is the stand-in.
        var ownSize = new Vector2(lastWidth, unit->GetScaledHeight(true));

        var placement = AddonPlacement.Of(unit);
        Position = AddonAnchor.Beside(
            placement.Position, placement.Width, plugin.Configuration.LootCompanionSide, ownSize,
            ImGui.GetIO().DisplaySize);

        PositionCondition = ImGuiCond.Always;
    }

    public override void Draw()
    {
        lastWidth = ImGui.GetWindowWidth();

        // Resolved again rather than kept from DrawConditions: the two run a frame apart under
        // Dalamud's window system, and a roll that ended in between would be drawn from a list the
        // addon no longer has.
        if (plugin.Loot.Resolve() is not { } rolls)
            return;

        ImGuiRenderer.Draw(plugin, LootCompanionPresenter.Build(rolls, plugin.Ownership));
    }
}
