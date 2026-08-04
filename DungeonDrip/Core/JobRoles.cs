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

    private static readonly IReadOnlyList<uint> NoJobs = [];

    private readonly Dictionary<uint, IReadOnlyList<RoleGroup>> groupByCategory;
    private readonly Dictionary<string, IReadOnlyList<uint>> jobsByGroup;

    private JobRoleIndex(
        Dictionary<uint, IReadOnlyList<RoleGroup>> groupByCategory,
        Dictionary<string, IReadOnlyList<uint>> jobsByGroup)
    {
        this.groupByCategory = groupByCategory;
        this.jobsByGroup = jobsByGroup;
    }

    /// <summary>
    /// The headings a piece belongs under. Usually one, but gear shared across roles lands in each
    /// role's heading rather than a combined one of its own.
    /// </summary>
    public IReadOnlyList<RoleGroup> GroupsFor(uint classJobCategoryRow) =>
        groupByCategory.TryGetValue(classJobCategoryRow, out var groups) ? groups : UnknownOnly;

    /// <summary>
    /// ClassJob rows for the jobs a heading can be queued as, so a caller can ask what level the
    /// character has in it. Classes and the limited jobs are absent: no roulette takes one.
    /// </summary>
    public IReadOnlyList<uint> JobsIn(RoleGroup group) =>
        jobsByGroup.TryGetValue(group.Label, out var jobs) ? jobs : NoJobs;

    private readonly record struct JobEntry(
        uint RowId, uint ParentRowId, string Abbreviation, LootRole Role, bool IsFullJob, bool IsLimited);

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
                // ClassJobParent points a job at the class it grew out of, and is self-referential
                // for a class - which is what lets Paladin claim gear the sheet only marks GLA.
                jobs.Add(new JobEntry(
                    job.RowId, job.ClassJobParent.RowId, abbreviation, role.Value,
                    job.JobIndex > 0, job.IsLimitedJob));
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

        // Jobs a roulette can actually be queued as: no classes, and no Blue Mage or Beastmaster.
        // Left in sheet order, which is the game's own, so advice lists jobs the way the character
        // sheet does rather than however a dictionary happened to fill.
        var playable = jobs.Where(job => job.IsFullJob && !job.IsLimited).ToList();

        var groups = new Dictionary<uint, IReadOnlyList<RoleGroup>>();
        var perGroup = new Dictionary<string, HashSet<uint>>();

        foreach (var (categoryRow, present) in members)
        {
            var classified = Classify(present, meleeOrder);
            groups[categoryRow] = [.. classified.Select(entry => entry.Group)];

            // One heading is reached from many categories, so the jobs behind it accumulate across
            // all of them. Old categories list only the classes ("GLA PGL MRD LNC ARC ROG MNK WAR
            // DRG BRD NIN" is a real row) and low-level dungeon gear still uses them, so the parent
            // is read as well as the job - otherwise Paladin drops out of the Tank heading there.
            foreach (var (group, membership) in classified)
            {
                var rows = membership.Select(job => job.RowId).ToHashSet();
                if (!perGroup.TryGetValue(group.Label, out var behind))
                    perGroup[group.Label] = behind = [];

                foreach (var job in playable)
                {
                    if (rows.Contains(job.RowId) || rows.Contains(job.ParentRowId))
                        behind.Add(job.RowId);
                }
            }
        }

        var jobsByGroup = perGroup.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyList<uint>)[.. playable
                .Where(job => entry.Value.Contains(job.RowId))
                .Select(job => job.RowId)]);

        Plugin.Log.Information(
            $"Built job role index: {groups.Count} categories, {playable.Count} queueable jobs, " +
            $"{meleeSets.Count} melee gear types ({string.Join("; ", meleeSets)})");

        return new JobRoleIndex(groups, jobsByGroup);
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
    ///
    /// Each heading is returned with the jobs that produced it, so a caller can ask what the player
    /// has levelled in it without having to work backwards from the label.
    /// </remarks>
    private static List<(RoleGroup Group, List<JobEntry> Membership)> Classify(
        List<JobEntry> present, IReadOnlyDictionary<string, int> meleeOrder)
    {
        var roles = present.Select(job => job.Role).ToHashSet();

        if (roles.Count == 0)
            return [(Unknown, present)];

        if (roles.Count == Enum.GetValues<LootRole>().Length)
            return [(new RoleGroup(AnyRoleOrder, "Any role", string.Empty), present)];

        var groups = new List<(RoleGroup, List<JobEntry>)>();
        foreach (var role in roles.OrderBy(r => Array.IndexOf(SharedLabelOrder, r)))
        {
            var membership = present.Where(job => job.Role == role).ToList();

            if (role != LootRole.Melee)
            {
                groups.Add((role switch
                {
                    LootRole.Tank => new RoleGroup(TankOrder, "Tank", string.Empty),
                    LootRole.Healer => new RoleGroup(HealerOrder, "Healer", string.Empty),
                    LootRole.PhysicalRanged => new RoleGroup(PhysicalRangedOrder, "Physical Ranged", string.Empty),
                    _ => new RoleGroup(MagicalRangedOrder, "Magical Ranged", string.Empty),
                }, membership));

                continue;
            }

            var set = LabelJobs(membership);
            groups.Add((new RoleGroup(
                meleeOrder.TryGetValue(set, out var order) ? order : MeleeOrderBase,
                $"Melee DPS ({set})",
                set), membership));
        }

        return groups;
    }

}
