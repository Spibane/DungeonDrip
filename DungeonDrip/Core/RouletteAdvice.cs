using System;
using System.Collections.Generic;
using System.Linq;
using DungeonDrip.Data;
using DungeonDrip.Game;
using Lumina.Excel.Sheets;

namespace DungeonDrip.Core;

/// <summary>How one role would fare in one roulette.</summary>
/// <param name="Total">Pieces in the pool this role could roll Need on, collected or not.</param>
/// <param name="Missing">How many of those you have not collected.</param>
public sealed record RoleOdds(string Label, int Order, int Missing, int Total)
{
    /// <summary>The share of what this role can roll on that would be new to you.</summary>
    public float Share => Total == 0 ? 0f : (float)Missing / Total;
}

/// <param name="DutyCount">Duties in the pool we hold loot for.</param>
/// <param name="PoolCount">Duties in the pool altogether.</param>
/// <param name="Roles">
/// Every role eligible to queue this, best first. Empty when nothing of yours clears the level bar.
/// </param>
public sealed record RouletteAdvice(
    string Name,
    byte RequiredLevel,
    int DutyCount,
    int PoolCount,
    IReadOnlyList<RoleOdds> Roles);

/// <summary>
/// Answers "what should I queue this roulette as" by counting uncollected gear across everything
/// the roulette can send you to.
/// </summary>
/// <remarks>
/// Counted by role heading rather than by job, which is both shorter to read and the same division
/// the missing list already draws. Melee stays split by gear type there, so "Melee DPS (NIN VPR)"
/// still names what to queue as rather than lumping pools that share nothing.
///
/// Ranked by share rather than by raw count. The question is how likely the next piece you can roll
/// on is to be one you do not have, and a raw count would simply crown whichever role the game
/// happens to hand the most gear - a role whose pool is small is not thereby a worse bet. Both
/// numbers are reported, because they disagree occasionally and the count is what you are actually
/// collecting.
///
/// Two things it cannot see. It does not know which duties you have unlocked, so a pool is assumed
/// open once your level clears it - a returning player's odds will be off in the direction of
/// optimism. And it counts what a role may roll on, not what will drop: a duty gives out a handful
/// of pieces per run, so these are the odds per roll, not per run.
/// </remarks>
public sealed class RouletteAdviceBuilder(
    DungeonLootData loot,
    ContentFinderIndex duties,
    OutfitCatalog outfits,
    JobRoleIndex jobRoles,
    StorageEligibility storage,
    EquipLockFilter equipLocks)
{
    public IReadOnlyList<RouletteAdvice> Build(OwnershipView ownership, Configuration configuration)
    {
        var items = Plugin.DataManager.GetExcelSheet<Item>();
        var classJobs = Plugin.DataManager.GetExcelSheet<ClassJob>();
        var levels = new Dictionary<string, int>();

        var advice = new List<RouletteAdvice>(duties.Roulettes.Count);

        foreach (var pool in duties.Roulettes)
        {
            var total = new Dictionary<RoleGroup, int>();
            var missing = new Dictionary<RoleGroup, int>();
            var covered = 0;

            foreach (var duty in pool.Duties)
            {
                if (!loot.TryGetItems(duty.TerritoryId, out var itemIds))
                    continue;

                covered++;

                // A roulette will not send you anywhere your job is too low for, so the whole duty
                // drops out for a role you have nothing levelled enough in.
                var needed = Math.Max(pool.RequiredLevel, duty.Level);

                foreach (var itemId in itemIds)
                {
                    if (!items.TryGetRow(itemId, out var item))
                        continue;

                    // The same filters the missing list uses, so the two views cannot disagree about
                    // what counts as a piece worth having. The job filter is deliberately not among
                    // them - the whole question here is which job to queue as, so a pool cannot be
                    // judged by the one you happen to be standing in.
                    if (!storage.MatchesScope(storage.Of(item), configuration.Scope))
                        continue;

                    if (equipLocks.Hides(item, configuration))
                        continue;

                    var (slot, _) = EquipSlots.Describe(item.EquipSlotCategory.Value);
                    if (configuration.HideWeapons && EquipSlots.IsWeaponSlot(slot))
                        continue;

                    var owned = MissingItems.Resolve(
                        itemId, ownership, outfits.SetsContaining(itemId),
                        configuration.OutfitOwnership, configuration.Scope) != OwnershipSource.None;

                    // The same headings the missing list uses, so a piece shared between roles is
                    // counted under each one that can roll on it, exactly as it is drawn there.
                    foreach (var group in jobRoles.GroupsFor(item.ClassJobCategory.RowId))
                    {
                        if (LevelOf(group) < needed)
                            continue;

                        total[group] = total.GetValueOrDefault(group) + 1;

                        if (!owned)
                            missing[group] = missing.GetValueOrDefault(group) + 1;
                    }
                }
            }

            advice.Add(new RouletteAdvice(
                pool.Name,
                pool.RequiredLevel,
                covered,
                pool.Duties.Count,
                [.. total
                    .Select(entry => new RoleOdds(
                        entry.Key.Label, entry.Key.Order,
                        missing.GetValueOrDefault(entry.Key), entry.Value))
                    .OrderByDescending(odds => odds.Share)
                    .ThenByDescending(odds => odds.Missing)
                    .ThenBy(odds => odds.Order)]));
        }

        return advice;

        // The best you have in the role, since queueing as it only needs one job high enough.
        // Memoised: levelling is slow enough that reading it once per rebuild is plenty, and this
        // would otherwise run once per item per heading.
        int LevelOf(RoleGroup group)
        {
            if (levels.TryGetValue(group.Label, out var known))
                return known;

            // Nothing to gate on without a loaded character, and nothing behind a heading that no
            // queueable job serves - better a full list than an empty one in either case.
            var jobs = jobRoles.JobsIn(group);
            var level = !Plugin.PlayerState.IsLoaded || jobs.Count == 0
                ? int.MaxValue
                : jobs.Max(row => classJobs.TryGetRow(row, out var job)
                    ? Plugin.PlayerState.GetClassJobLevel(job)
                    : (short)0);

            return levels[group.Label] = level;
        }
    }
}
