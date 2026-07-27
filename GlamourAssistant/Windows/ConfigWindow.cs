using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace GlamourAssistant.Windows;

public class ConfigWindow : Window, IDisposable
{
    private static readonly Vector4 Muted = new(0.60f, 0.60f, 0.60f, 1f);

    private readonly Plugin plugin;
    private readonly Configuration configuration;

    public ConfigWindow(Plugin plugin)
        : base("Glamour Assistant Settings###GlamourAssistantConfig", ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.plugin = plugin;
        configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public override void Draw()
    {
        var changed = false;

        ImGui.TextColored(Muted, "Window");
        var autoOpen = configuration.AutoOpenOnDutyEnter;
        if (ImGui.Checkbox("Open automatically when I enter a duty", ref autoOpen))
        {
            configuration.AutoOpenOnDutyEnter = autoOpen;
            changed = true;
        }

        var hideWhenComplete = configuration.HideWhenNothingMissing;
        if (ImGui.Checkbox("...but not when I already have everything", ref hideWhenComplete))
        {
            configuration.HideWhenNothingMissing = hideWhenComplete;
            changed = true;
        }

        var showOwned = configuration.ShowOwnedItems;
        if (ImGui.Checkbox("List pieces I already have, greyed out", ref showOwned))
        {
            configuration.ShowOwnedItems = showOwned;
            changed = true;
        }

        var jobOnly = configuration.OnlyCurrentJobEquippable;
        if (ImGui.Checkbox("Only show gear my current job can wear", ref jobOnly))
        {
            configuration.OnlyCurrentJobEquippable = jobOnly;
            changed = true;
        }

        ImGui.Spacing();
        ImGui.TextColored(Muted, "What counts as owned");

        var countInventory = configuration.CountInventoryAndEquipped;
        if (ImGui.Checkbox("Bags, armoury chest, equipped gear and saddlebags", ref countInventory))
        {
            configuration.CountInventoryAndEquipped = countInventory;
            changed = true;
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "Off by default: a drop sitting in your bag is not in your collection yet.\n" +
                "Retainer inventories cannot be read unless you are at a retainer, so they are never counted.");
        }

        ImGui.Spacing();
        ImGui.Text("Pieces stored inside outfit sets:");

        var mode = configuration.OutfitOwnership;
        if (ImGui.RadioButton("Owned if any stored outfit contains the piece", ref mode, OutfitOwnershipMode.AnyOutfit))
        {
            configuration.OutfitOwnership = mode;
            changed = true;
        }

        if (ImGui.RadioButton("Owned only if every outfit with that piece is stored", ref mode, OutfitOwnershipMode.AllOutfits))
        {
            configuration.OutfitOwnership = mode;
            changed = true;
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "A piece can appear in more than one outfit set. The strict option only calls it\n" +
                "collected once you have stored every set that includes it.");
        }

        ImGui.Spacing();
        ImGui.TextColored(Muted, "Collection snapshot");

        var staleDays = configuration.StaleAfterDays;
        ImGui.SetNextItemWidth(160 * Dalamud.Interface.Utility.ImGuiHelpers.GlobalScale);
        if (ImGui.SliderInt("Warn when dresser data is older than (days)", ref staleDays, 1, 60))
        {
            configuration.StaleAfterDays = staleDays;
            changed = true;
        }

        ImGui.TextWrapped(
            "The game clears Glamour Dresser data every time you change zone and only loads the Armoire " +
            "when you open it, so Glamour Assistant remembers the last time it saw them.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextColored(Muted, "Dungeon loot data");

        ImGui.TextWrapped(plugin.LootData.StatusMessage);

        var data = plugin.LootData.Data;
        if (data != null)
            ImGui.TextColored(Muted, $"{data.DutyCount} duties, downloaded {data.FetchedUtc:yyyy-MM-dd HH:mm} UTC.");

        ImGui.TextWrapped(
            "The list is downloaded from FFXIV Teamcraft's public data every time the plugin loads, " +
            "so new dungeons arrive without a plugin update. A copy is kept on disk for offline use.");

        if (ImGui.Button("Check for updates now"))
            plugin.LootData.CheckForUpdates(force: true);

        ImGui.Spacing();
        ImGui.TextWrapped(
            "Missing drops for a very recent dungeon? Upstream lags on brand-new content. Add them to " +
            "loot-overrides.json in the plugin config folder and reload the plugin.");

        if (ImGui.Button("Open config folder"))
            OpenConfigFolder();

        if (changed)
        {
            configuration.Save();
            plugin.InvalidateReport();
        }
    }

    private static void OpenConfigFolder()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = Plugin.PluginInterface.GetPluginConfigDirectory(),
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Could not open the plugin config folder");
        }
    }
}
