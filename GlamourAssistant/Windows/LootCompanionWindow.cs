using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.UI;
using GlamourAssistant.Core;
using Lumina.Excel.Sheets;

namespace GlamourAssistant.Windows;

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
public sealed unsafe class LootCompanionWindow : Window, IDisposable
{
    private static readonly Vector4 Missing = new(1.00f, 0.85f, 0.40f, 1f);
    private static readonly Vector4 Owned = new(0.55f, 0.55f, 0.55f, 1f);
    private static readonly Vector4 Warning = new(1.00f, 0.71f, 0.20f, 1f);

    private const float GapPixels = 6f;

    private readonly Plugin plugin;

    /// <summary>
    /// Our own width, measured at the end of the last frame. PreDraw runs before this window's
    /// Begin, so ImGui's current-window queries are not ours to ask there.
    /// </summary>
    private float lastWidth = 220f;

    public LootCompanionWindow(Plugin plugin)
        : base("Still needed###GlamourAssistantLootCompanion",
               ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse |
               ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing |
               ImGuiWindowFlags.NoNav)
    {
        this.plugin = plugin;
        RespectCloseHotkey = false;
        ShowCloseButton = false;
        DisableWindowSounds = true;
    }

    public void Dispose() { }

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
        var width = unit->GetScaledWidth(true);
        var height = unit->GetScaledHeight(true);

        var ownWidth = lastWidth;

        var side = plugin.Configuration.LootCompanionSide;
        var toRight = side switch
        {
            LootCompanionSide.Left => false,
            LootCompanionSide.Right => true,
            _ => unit->X + width + GapPixels + ownWidth <= ImGui.GetIO().DisplaySize.X,
        };

        var x = toRight
            ? unit->X + width + GapPixels
            : unit->X - ownWidth - GapPixels;

        // Keep it on screen even if the loot window is jammed against an edge.
        x = Math.Clamp(x, 0f, Math.Max(0f, ImGui.GetIO().DisplaySize.X - ownWidth));
        var y = Math.Clamp((float)unit->Y, 0f, Math.Max(0f, ImGui.GetIO().DisplaySize.Y - height));

        Position = new Vector2(x, y);
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
            ImGui.TextColored(Warning, "No dresser data.");
            ImGui.TextColored(Warning, "Open your Glamour Dresser.");
            return;
        }

        if (ownership.IsDresserStale)
        {
            ImGui.TextColored(Warning, "Dresser data is stale -");
            ImGui.TextColored(Warning, "these may be wrong.");
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
            var itemId = Game.ItemId.Normalize(addon->Items[i].ItemId);
            if (itemId == 0 || !items.TryGetRow(itemId, out var item))
                continue;

            // Only what the chosen store can hold; the rest of the roll list is not our business.
            var storage = plugin.Storage;
            if (!storage.MatchesScope(storage.Of(item), plugin.Configuration.Scope))
                continue;

            var source = MissingItems.Resolve(
                itemId, view, outfits.SetsContaining(itemId), mode, plugin.Configuration.Scope);
            var owned = source != OwnershipSource.None;
            if (!owned)
                anyMissing = true;

            DrawRow(item, owned);
        }

        if (!anyMissing)
            ImGui.TextColored(Owned, "Nothing new here.");
    }

    private static void DrawRow(Item item, bool owned)
    {
        var iconSize = new Vector2(20, 20) * ImGuiHelpers.GlobalScale;
        var icon = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(item.Icon)).GetWrapOrDefault();

        if (icon != null)
            ImGui.Image(icon.Handle, iconSize);
        else
            ImGui.Dummy(iconSize);

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();

        if (owned)
            ImGui.TextColored(Owned, item.Name.ExtractText());
        else
            ImGui.TextColored(Missing, $"{item.Name.ExtractText()}  ←");
    }

    private static AddonNeedGreed* GetLootAddon() =>
        Plugin.GameGui.GetAddonByName<AddonNeedGreed>("NeedGreed");
}
