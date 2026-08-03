using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.UI;
using DungeonDrip.Core;
using Lumina.Excel.Sheets;

namespace DungeonDrip.Windows;

/// <summary>
/// A small window that rides alongside the Need/Greed roll window, marking which of the items up
/// for roll you do not own yet.
/// </summary>
/// <remarks>
/// Deliberately a separate window rather than recolouring the loot addon's own nodes. Several
/// popular plugins (Allagan Tools, Simple Tweaks, VanillaPlus, Collections) already write to game
/// addon nodes, and two plugins fighting over the same node's colour is a real conflict - whoever
/// writes last in a frame wins, and a refresh can revert either. This reads the addon's item list
/// and screen position and draws nothing into it, so by construction there is nothing to collide
/// with. It also survives the addon's node layout changing in a patch.
/// </remarks>
public sealed unsafe class LootCompanionWindow : Window
{
    private readonly Plugin plugin;

    /// <summary>
    /// Our own width, measured at the end of the last frame. PreDraw runs before this window's
    /// Begin, so ImGui's current-window queries are not ours to ask there.
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
    public override bool DrawConditions()
    {
        if (!plugin.Configuration.ShowLootCompanion)
            return false;

        var addon = GetLootAddon();
        return addon != null && addon->AtkUnitBase.IsVisible && addon->NumItems > 0;
    }

    public override void PreDraw()
    {
        var addon = GetLootAddon();
        if (addon == null)
            return;

        var unit = &addon->AtkUnitBase;

        // Auto-sized, so its own height is not knowable here; the roll window's is the stand-in.
        var ownSize = new Vector2(lastWidth, unit->GetScaledHeight(true));

        Position = AddonAnchor.Beside(unit, plugin.Configuration.LootCompanionSide, ownSize);
        PositionCondition = ImGuiCond.Always;
    }

    public override void Draw()
    {
        lastWidth = ImGui.GetWindowWidth();

        var addon = GetLootAddon();
        if (addon == null)
            return;

        var ownership = plugin.Ownership;

        // A roll is time-critical and unverifiable in the moment, so a stale snapshot must not be
        // presented as fact - say so instead of showing a confident guess.
        if (!ownership.HasDresserData)
        {
            ImGui.TextColored(Palette.Warning, "No dresser data.");
            ImGui.TextColored(Palette.Warning, "Open your Glamour Dresser.");
            return;
        }

        if (ownership.IsDresserStale)
        {
            ImGui.TextColored(Palette.Warning, "Dresser data is stale -");
            ImGui.TextColored(Palette.Warning, "these may be wrong.");
            ImGui.Separator();
        }

        var items = Plugin.DataManager.GetExcelSheet<Item>();
        var view = ownership.Current;
        var outfits = plugin.Outfits;
        var mode = plugin.Configuration.OutfitOwnership;

        var count = Math.Min(addon->NumItems, addon->Items.Length);
        var anyMissing = false;

        for (var i = 0; i < count; i++)
        {
            var (itemId, kind) = ItemUtil.GetBaseId(addon->Items[i].ItemId);
            if (itemId == 0 || kind == ItemKind.EventItem || !items.TryGetRow(itemId, out var item))
                continue;

            // Only what the chosen store can hold; the rest of the roll list is not our business.
            var storage = plugin.Storage;
            if (!storage.MatchesScope(storage.Of(item), plugin.Configuration.Scope))
                continue;

            // The job filter is deliberately absent here - anyone may roll on anything - but a piece
            // this character can never wear is not a piece to spend a Need on.
            if (plugin.EquipLocks.Hides(item, plugin.Configuration))
                continue;

            var source = MissingItems.Resolve(
                itemId, view, outfits.SetsContaining(itemId), mode, plugin.Configuration.Scope);
            var owned = source != OwnershipSource.None;
            if (!owned)
                anyMissing = true;

            DrawRow(item, owned);
        }

        if (!anyMissing)
            ImGui.TextColored(Palette.Muted, "Nothing new here.");
    }

    private void DrawRow(Item item, bool owned)
    {
        var name = item.Name.ExtractText();

        UiParts.ItemIcon(item.Icon, 20);

        if (owned)
            ImGui.TextColored(Palette.Muted, name);
        else
            ImGui.TextColored(Palette.Missing, $"{name}  ←");

        UiParts.ItemContextMenu(plugin, item.RowId, name);
    }

    private static AddonNeedGreed* GetLootAddon() =>
        Plugin.GameGui.GetAddonByName<AddonNeedGreed>("NeedGreed");
}
