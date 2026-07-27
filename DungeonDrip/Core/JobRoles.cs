using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Lumina.Excel.Sheets;

namespace DungeonDrip.Core;

public enum LootRole
{
    Tank,
    Healer,
    Melee,
    PhysicalRanged,
    MagicalRanged,
}

/// <summary>
/// Buckets a piece by who is allowed to roll Need on it.
/// </summary>
/// <remarks>
/// Two things the game models less simply than it looks:
///
/// ClassJob.Role only distinguishes tank / melee / ranged / healer - it lumps Bard in with Black
/// Mage. PrimaryStat splits those apart (DEX for physical ranged, INT for casters).
///
/// "Melee DPS" is not one bucket either. Maiming, Striking and Scouting gear go to different jobs
/// and share nothing, so a single melee heading is useless for claiming during a run. Rather than
/// hardcode which jobs wear what - that changes every expansion, Viper being the most recent - melee
/// categories are split by the job set the game itself lists on the item. Tanks and healers stay
/// whole because their armour and accessories genuinely share one category, so this splits exactly
/// where the game splits and nowhere else.
/// </remarks>
public sealed class JobRoleIndex
{
    private const int DexterityStat = 2;

    // Spaced out because the melee band holds one slot per distinct gear type, and the sheet has
    // more melee-only categories than the four the current dungeon sets use.
    private const int TankOrder = 0;
    private const int HealerOrder = 1000;
    private const int MeleeOrderBase = 2000;
    private const int PhysicalRangedOrder = 3000;
    private const int MagicalRangedOrder = 4000;
    private const int MixedOrder = 8000;
    private const int AnyRoleOrder = 9000;
    private const int UnknownOrder = 9900;

    private static readonly (int Order, string Label) Unknown = (UnknownOrder, "Anyone");

    /// <summary>
    /// Order roles appear in within a shared heading, primary owner first.
    /// </summary>
    /// <remarks>
    /// Melee comes last because the case that actually occurs is "of Aiming": a ranged gear line
    /// that NIN and VPR happen to share, so it should read Physical Ranged / Melee DPS rather than
    /// implying the pieces are melee gear.
    /// </remarks>
    private static readonly LootRole[] SharedLabelOrder =
    [
        LootRole.Tank,
        LootRole.Healer,
        LootRole.PhysicalRanged,
        LootRole.MagicalRanged,
        LootRole.Melee,
    ];

    private readonly Dictionary<uint, (int Order, string Label)> groupByCategory;

    private JobRoleIndex(Dictionary<uint, (int Order, string Label)> groupByCategory) =>
        this.groupByCategory = groupByCategory;

    /// <summary>The heading a piece belongs under, and where that heading sorts.</summary>
    public (int Order, string Label) GroupFor(uint classJobCategoryRow) =>
        groupByCategory.TryGetValue(classJobCategoryRow, out var group) ? group : Unknown;

    private readonly record struct JobEntry(string Abbreviation, LootRole Role, bool IsFullJob);

    public static JobRoleIndex Build()
    {
        var jobs = new List<JobEntry>();

        foreach (var job in Plugin.DataManager.GetExcelSheet<ClassJob>())
        {
            var abbreviation = job.Abbreviation.ExtractText();
            if (string.IsNullOrWhiteSpace(abbreviation))
                continue;

            var role = job.Role switch
            {
                1 => LootRole.Tank,
                4 => LootRole.Healer,
                2 => LootRole.Melee,
                3 => job.PrimaryStat == DexterityStat ? LootRole.PhysicalRanged : LootRole.MagicalRanged,
                _ => (LootRole?)null,
            };

            if (role.HasValue)
            {
                // JobIndex is 0 for the base classes, so labels can name Dragoon without Lancer.
                jobs.Add(new JobEntry(abbreviation, role.Value, job.JobIndex > 0));
            }
        }

        // ClassJobCategory exposes one boolean column per job abbreviation; resolve them once.
        var columns = jobs
            .Select(job => (Job: job, Property: typeof(ClassJobCategory)
                .GetProperty(job.Abbreviation, BindingFlags.Public | BindingFlags.Instance)))
            .Where(c => c.Property != null)
            .ToList();

        var members = new Dictionary<uint, List<JobEntry>>();
        foreach (var category in Plugin.DataManager.GetExcelSheet<ClassJobCategory>())
        {
            var present = columns
                .Where(c => c.Property!.GetValue(category) is true)
                .Select(c => c.Job)
                .ToList();

            if (present.Count > 0)
                members[category.RowId] = present;
        }

        // Melee splits by job set. Index them up front so every category resolves to a stable order.
        var meleeSets = members.Values
            .Where(IsMeleeOnly)
            .Select(LabelJobs)
            .Distinct()
            .OrderBy(set => set.Count(c => c == ' '))
            .ThenBy(set => set, StringComparer.Ordinal)
            .ToList();

        var meleeOrder = meleeSets
            .Select((set, index) => (set, index))
            .ToDictionary(x => x.set, x => MeleeOrderBase + x.index);

        var groups = new Dictionary<uint, (int, string)>();
        foreach (var (categoryRow, present) in members)
            groups[categoryRow] = Classify(present, meleeOrder);

        Plugin.Log.Information(
            $"Built job role index: {groups.Count} categories, {meleeSets.Count} melee gear types " +
            $"({string.Join("; ", meleeSets)})");

        return new JobRoleIndex(groups);
    }

    private static bool IsMeleeOnly(List<JobEntry> jobs) => jobs.All(job => job.Role == LootRole.Melee);

    /// <summary>Names the jobs, dropping base classes so "LNC DRG RPR" reads as "DRG RPR".</summary>
    private static string LabelJobs(List<JobEntry> jobs)
    {
        var named = jobs.Where(job => job.IsFullJob).Select(job => job.Abbreviation).ToList();
        if (named.Count == 0)
            named = jobs.Select(job => job.Abbreviation).ToList();

        return string.Join(' ', named);
    }

    private static (int Order, string Label) Classify(
        List<JobEntry> present, IReadOnlyDictionary<string, int> meleeOrder)
    {
        var roles = present.Select(job => job.Role).ToHashSet();

        if (roles.Count == 0)
            return Unknown;

        if (roles.Count > 1)
        {
            return roles.Count == Enum.GetValues<LootRole>().Length
                ? (AnyRoleOrder, "Any role")
                : (MixedOrder, string.Join(" / ", roles.OrderBy(r => Array.IndexOf(SharedLabelOrder, r)).Select(NameOf)));
        }

        var role = roles.First();
        if (role != LootRole.Melee)
        {
            return role switch
            {
                LootRole.Tank => (TankOrder, "Tank"),
                LootRole.Healer => (HealerOrder, "Healer"),
                LootRole.PhysicalRanged => (PhysicalRangedOrder, "Physical Ranged"),
                _ => (MagicalRangedOrder, "Magical Ranged"),
            };
        }

        var set = LabelJobs(present);
        return (meleeOrder.TryGetValue(set, out var order) ? order : MeleeOrderBase, $"Melee DPS ({set})");
    }

    public static string NameOf(LootRole role) => role switch
    {
        LootRole.Tank => "Tank",
        LootRole.Healer => "Healer",
        LootRole.Melee => "Melee DPS",
        LootRole.PhysicalRanged => "Physical Ranged",
        LootRole.MagicalRanged => "Magical Ranged",
        _ => "Unknown",
    };
}
