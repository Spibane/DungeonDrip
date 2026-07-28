using System;
using System.Collections.Generic;
using System.Linq;
using DungeonDrip.Data;
using DungeonDrip.Game;
using Lumina.Excel.Sheets;

namespace DungeonDrip.Core;

/// <summary>How one job would fare in one roulette.</summary>
/// <param name="Total">Pieces in the pool this job could roll Need on, collected or not.</param>
/// <param name="Missing">How many of those you have not collected.</param>
public sealed record JobOdds(string Abbreviation, int Missing, int Total)
{
    /// <summary>The share of what this job can roll on that would be new to you.</summary>
    public float Share => Total == 0 ? 0f : (float)Missing / Total;
}

/// <param name="DutyCount">Duties in the pool we hold loot for.</param>
/// <param name="PoolCount">Duties in the pool altogether.</param>
/// <param name="Jobs">
/// Every job eligible to queue this, best first. Empty when no job of yours clears the level bar.
/// </param>
public sealed record RouletteAdvice(
    string Name,
    byte RequiredLevel,
    int DutyCount,
    int PoolCount,
    IReadOnlyList<JobOdds> Jobs);

/// <summary>
/// Answers "what should I queue this roulette as" by counting uncollected gear across everything
/// the roulette can send you to.
/// </summary>
/// <remarks>
/// Ranked by share rather than by raw count. The question is how likely the next piece you can roll
/// on is to be one you do not have, and a raw count would simply crown whichever role the game
/// happens to hand the most gear - jobs whose pool is small are not thereby worse bets. Both
/// numbers are reported, because they disagree occasionally and the count is what you are actually
/// collecting.
///
/// Two things it cannot see. It does not know which duties you have unlocked, so a pool is assumed
/// open once your level clears it - a returning player's odds will be off in the direction of
/// optimism. And it counts what a job may roll on, not what will drop: a duty gives out a handful
/// of pieces per run, so these are the odds per roll, not per run.
/// </remarks>
public sealed class RouletteAdviceBuilder(
    DungeonLootData loot,
    ContentFinderIndex duties,
    OutfitCatalog outfits,
    JobRoleIndex jobRoles,
    StorageEligibility storage)
{
    public IReadOnlyList<RouletteAdvice> Build(OwnershipView ownership, Configuration configuration)
    {
        var items = Plugin.DataManager.GetExcelSheet<Item>();
        var classJobs = Plugin.DataManager.GetExcelSheet<ClassJob>();
        var levels = new Dictionary<uint, int>();

        var advice = new List<RouletteAdvice>(duties.Roulettes.Count);

        foreach (var pool in duties.Roulettes)
        {
            var total = new Dictionary<uint, int>();
            var missing = new Dictionary<uint, int>();
            var names = new Dictionary<uint, string>();
            var covered = 0;

            foreach (var duty in pool.Duties)
            {
                if (!loot.TryGetItems(duty.TerritoryId, out var itemIds))
                    continue;

                covered++;

                // A roulette will not send you anywhere your job is too low for, so the whole duty
                // drops out for a job that cannot be queued into it.
                var needed = Math.Max(pool.RequiredLevel, duty.Level);

                foreach (var itemId in itemIds)
                {
                    if (!items.TryGetRow(itemId, out var item))
                        continue;

                    // Same two filters the missing list uses, so the two views cannot disagree
                    // about what counts as a piece worth having.
                    if (!storage.MatchesScope(storage.Of(item), configuration.Scope))
                        continue;

                    var (slot, _) = EquipSlots.Describe(item.EquipSlotCategory.Value);
                    if (configuration.HideWeapons && EquipSlots.IsWeaponSlot(slot))
                        continue;

                    var owned = MissingItems.Resolve(
                        itemId, ownership, outfits.SetsContaining(itemId),
                        configuration.OutfitOwnership, configuration.Scope) != OwnershipSource.None;

                    foreach (var job in jobRoles.JobsFor(item.ClassJobCategory.RowId))
                    {
                        if (LevelOf(job.RowId) < needed)
                            continue;

                        names[job.RowId] = job.Abbreviation;
                        total[job.RowId] = total.GetValueOrDefault(job.RowId) + 1;

                        if (!owned)
                            missing[job.RowId] = missing.GetValueOrDefault(job.RowId) + 1;
                    }
                }
            }

            advice.Add(new RouletteAdvice(
                pool.Name,
                pool.RequiredLevel,
                covered,
                pool.Duties.Count,
                [.. total
                    .Select(entry => new JobOdds(
                        names[entry.Key], missing.GetValueOrDefault(entry.Key), entry.Value))
                    .OrderByDescending(odds => odds.Share)
                    .ThenByDescending(odds => odds.Missing)
                    .ThenBy(odds => odds.Abbreviation, StringComparer.Ordinal)]));
        }

        return advice;

        // Levelling is slow enough that reading each job once per rebuild is plenty, and the
        // lookup would otherwise run once per item per job.
        int LevelOf(uint jobRow)
        {
            if (levels.TryGetValue(jobRow, out var known))
                return known;

            // With no character loaded there is nothing to gate on, so nothing is gated - better a
            // full list than an empty one while the plugin waits for a login.
            var level = Plugin.PlayerState.IsLoaded && classJobs.TryGetRow(jobRow, out var job)
                ? Plugin.PlayerState.GetClassJobLevel(job)
                : int.MaxValue;

            return levels[jobRow] = level;
        }
    }
}
