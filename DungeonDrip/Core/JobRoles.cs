using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Lumina.Excel.Sheets;

namespace DungeonDrip.Core;

/// <summary>
/// A heading in the role-grouped list, and where it sorts.
/// </summary>
/// <param name="JobKey">
/// Space-separated jobs for a melee gear-type heading, empty otherwise. Lets callers tell that
/// "MNK DRG SAM RPR" covers "MNK SAM" without parsing the display label. Kept as a string so the
/// record still compares by value.
/// </param>
public readonly record struct RoleGroup(int Order, string Label, string JobKey)
{
    /// <summary>True when this heading's jobs are all served by <paramref name="other"/> as well.</summary>
    public bool IsCoveredBy(RoleGroup other)
    {
        if (JobKey.Length == 0 || other.JobKey.Length == 0 || JobKey == other.JobKey)
            return false;

        var covering = other.JobKey.Split(' ');
        return JobKey.Split(' ').All(job => covering.Contains(job));
    }
}

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

    private static readonly RoleGroup Unknown = new(UnknownOrder, "Anyone", string.Empty);
    private static readonly IReadOnlyList<RoleGroup> UnknownOnly = [Unknown];

    /// <summary>
    /// Order a shared piece's headings are listed in, primary owner first.
    /// </summary>
    /// <remarks>
    /// Melee last because the case that occurs is "of Aiming": a ranged line NIN and VPR also roll
    /// on. Headings are sorted globally before drawing, so this only fixes the order within one
    /// piece's own list - kept so that list is deterministic rather than hash-ordered.
    /// </remarks>
    private static readonly LootRole[] SharedLabelOrder =
    [
        LootRole.Tank,
        LootRole.Healer,
        LootRole.PhysicalRanged,
        LootRole.MagicalRanged,
        LootRole.Melee,
    ];

    private readonly Dictionary<uint, IReadOnlyList<RoleGroup>> groupByCategory;

    private JobRoleIndex(Dictionary<uint, IReadOnlyList<RoleGroup>> groupByCategory) =>
        this.groupByCategory = groupByCategory;

    /// <summary>
    /// The headings a piece belongs under. Usually one, but gear shared across roles lands in each
    /// role's heading rather than a combined one of its own.
    /// </summary>
    public IReadOnlyList<RoleGroup> GroupsFor(uint classJobCategoryRow) =>
        groupByCategory.TryGetValue(classJobCategoryRow, out var groups) ? groups : UnknownOnly;

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

        var groups = new Dictionary<uint, IReadOnlyList<RoleGroup>>();
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

    /// <summary>
    /// Splits a category into the headings it belongs under, one per role present.
    /// </summary>
    /// <remarks>
    /// Gear shared between roles used to get a combined heading of its own, which left it in
    /// neither pile: "of Aiming" accessories are ranged gear that NIN and VPR also roll on, so a
    /// scouting player scanning their own heading never saw them. Each role now gets its own copy of
    /// the entry, and the melee side resolves to the gear-type heading for exactly those melee jobs -
    /// Aiming's melee members are ROG NIN VPR, which is the Scouting heading already on screen.
    /// </remarks>
    private static IReadOnlyList<RoleGroup> Classify(
        List<JobEntry> present, IReadOnlyDictionary<string, int> meleeOrder)
    {
        var roles = present.Select(job => job.Role).ToHashSet();

        if (roles.Count == 0)
            return UnknownOnly;

        if (roles.Count == Enum.GetValues<LootRole>().Length)
            return [new RoleGroup(AnyRoleOrder, "Any role", string.Empty)];

        var groups = new List<RoleGroup>();
        foreach (var role in roles.OrderBy(r => Array.IndexOf(SharedLabelOrder, r)))
        {
            if (role != LootRole.Melee)
            {
                groups.Add(role switch
                {
                    LootRole.Tank => new RoleGroup(TankOrder, "Tank", string.Empty),
                    LootRole.Healer => new RoleGroup(HealerOrder, "Healer", string.Empty),
                    LootRole.PhysicalRanged => new RoleGroup(PhysicalRangedOrder, "Physical Ranged", string.Empty),
                    _ => new RoleGroup(MagicalRangedOrder, "Magical Ranged", string.Empty),
                });

                continue;
            }

            var set = LabelJobs(present.Where(job => job.Role == LootRole.Melee).ToList());
            groups.Add(new RoleGroup(
                meleeOrder.TryGetValue(set, out var order) ? order : MeleeOrderBase,
                $"Melee DPS ({set})",
                set));
        }

        return groups;
    }

}
