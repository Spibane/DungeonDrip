using System;
using System.Collections.Generic;
using DungeonDrip.Core;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.UI;
using Lumina.Excel.Sheets;

namespace DungeonDrip.Game;

/// <summary>Reads the Need/Greed window and says which of what is up for roll is not collected.</summary>
/// <remarks>
/// Lifted out of the companion window when that window gained a second way of being drawn. It was
/// the last surface answering a question about the gear inside the code that drew it, and the
/// answer here is not a small one: which of the roll's items the chosen store can even hold, which
/// this character could never wear, and whether each is owned under the outfit rule in force.
/// Written a second time it would have drifted from the first, and a roll is the one list in the
/// plugin nobody can go back and check.
///
/// <b>Unlike the panel watchers, this holds no addon lifecycle registration.</b> A roll window opens
/// and closes many times in a dungeon and carries its whole contents in the addon itself, so there
/// is nothing to remember between appearances and nothing a lifecycle event would save. What is
/// cached is only so the native window can tell a list it has already built from a new one.
/// </remarks>
public sealed unsafe class LootRollWatcher(Plugin plugin)
{
    public const string AddonName = "NeedGreed";

    /// <summary>Reused every frame so the read path allocates nothing while nothing has changed.</summary>
    private readonly List<uint> scratch = new(16);

    private uint[] currentIds = [];
    private List<LootRoll>? rolls;
    private int seenRowRevision = -1;
    private OwnershipRules seenRules;

    /// <summary>
    /// What is up for roll, or null when there is no roll window to draw beside.
    /// </summary>
    /// <remarks>
    /// Empty and null are different answers and both happen: null is "no roll", where the window
    /// should not be up at all, and empty is "a roll of things this plugin has no opinion about",
    /// where it should say so.
    /// </remarks>
    public IReadOnlyList<LootRoll>? Resolve()
    {
        if (!plugin.Configuration.ShowLootCompanion || Plugin.GameGui.GameUiHidden)
            return null;

        var addon = Plugin.GameGui.GetAddonByName<AddonNeedGreed>(AddonName);
        if (addon == null || !addon->AtkUnitBase.IsVisible || addon->NumItems <= 0)
            return Forget();

        var count = Math.Min(addon->NumItems, addon->Items.Length);

        scratch.Clear();
        for (var index = 0; index < count; index++)
            scratch.Add(addon->Items[index].ItemId);

        plugin.Rows.Revalidate();

        var rules = Rules();

        if (rolls != null && plugin.Rows.Revision == seenRowRevision && rules == seenRules &&
            Unchanged())
        {
            return rolls;
        }

        currentIds = scratch.ToArray();
        seenRowRevision = plugin.Rows.Revision;
        seenRules = rules;
        rolls = Build();

        return rolls;
    }

    /// <summary>
    /// The settings that change the answer without changing what is being rolled on.
    /// </summary>
    /// <remarks>
    /// Compared as a whole so a setting flipped mid-roll reaches the list. The job filter is
    /// deliberately not among them - anyone may roll on anything - but a piece this character can
    /// never wear is not a piece to spend a Need on, so the equipment locks are.
    /// </remarks>
    private OwnershipRules Rules() => new(
        plugin.Configuration.Scope,
        plugin.Configuration.OutfitOwnership,
        plugin.Configuration.OnlyCurrentGenderEquippable,
        plugin.Configuration.OnlyCurrentRaceEquippable);

    private bool Unchanged()
    {
        if (currentIds.Length != scratch.Count)
            return false;

        for (var index = 0; index < currentIds.Length; index++)
        {
            if (currentIds[index] != scratch[index])
                return false;
        }

        return true;
    }

    /// <remarks>
    /// The order the addon lists them in, kept exactly. A roll is read against the window beside it
    /// and a list that sorted itself would have to be matched up row by row under time pressure.
    /// </remarks>
    private List<LootRoll> Build()
    {
        var items = Plugin.DataManager.GetExcelSheet<Item>();
        var view = plugin.Ownership.Current;
        var outfits = plugin.Outfits;
        var storage = plugin.Storage;
        var mode = plugin.Configuration.OutfitOwnership;
        var scope = plugin.Configuration.Scope;

        var built = new List<LootRoll>(currentIds.Length);

        foreach (var rolled in currentIds)
        {
            var (itemId, kind) = ItemUtil.GetBaseId(rolled);
            if (itemId == 0 || kind == ItemKind.EventItem || !items.TryGetRow(itemId, out var item))
                continue;

            // Only what the chosen store can hold; the rest of a roll is not this plugin's business.
            if (!storage.MatchesScope(storage.Of(item), scope))
                continue;

            if (plugin.EquipLocks.Hides(item, plugin.Configuration))
                continue;

            var source = MissingItems.Resolve(
                itemId, view, outfits.SetsContaining(itemId), mode, scope);

            built.Add(new LootRoll(itemId, item.Name.ExtractText(), item.Icon, source != OwnershipSource.None));
        }

        return built;
    }

    private List<LootRoll>? Forget()
    {
        rolls = null;
        currentIds = [];
        return null;
    }

    /// <summary>The settings a roll's answer depends on, as one comparable value.</summary>
    private readonly record struct OwnershipRules(
        CollectionScope Scope, OutfitOwnershipMode Outfits, bool GenderLocked, bool RaceLocked);
}
