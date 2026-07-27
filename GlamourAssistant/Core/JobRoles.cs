using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Lumina.Excel.Sheets;

namespace GlamourAssistant.Core;

public enum LootRole
{
    Tank,
    Healer,
    Melee,
    PhysicalRanged,
    MagicalRanged,
}

/// <summary>
/// Which roles are allowed to roll Need on a piece, derived from the jobs that can equip it.
/// </summary>
/// <remarks>
/// ClassJob.Role only distinguishes tank / melee / ranged / healer - it lumps Bard in with Black
/// Mage. PrimaryStat splits those apart cleanly (DEX for physical ranged, INT for casters), so the
/// five roles people actually call out in a dungeon come straight from game data with no hardcoded
/// job lists to rot.
/// </remarks>
public sealed class JobRoleIndex
{
    private const int DexterityStat = 2;

    private readonly Dictionary<uint, HashSet<LootRole>> rolesByCategory;

    private JobRoleIndex(Dictionary<uint, HashSet<LootRole>> rolesByCategory) =>
        this.rolesByCategory = rolesByCategory;

    private static readonly HashSet<LootRole> Empty = [];

    public IReadOnlySet<LootRole> RolesFor(uint classJobCategoryRow) =>
        rolesByCategory.TryGetValue(classJobCategoryRow, out var roles) ? roles : Empty;

    public static JobRoleIndex Build()
    {
        // Combat jobs paired with the ClassJobCategory column that names them.
        var jobs = new List<(string Abbreviation, LootRole Role)>();

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
                jobs.Add((abbreviation, role.Value));
        }

        // ClassJobCategory exposes one boolean column per job abbreviation, so membership is a
        // reflected read. Resolve the properties once, then it is just field access.
        var columns = jobs
            .Select(j => (j.Role, Property: typeof(ClassJobCategory)
                .GetProperty(j.Abbreviation, BindingFlags.Public | BindingFlags.Instance)))
            .Where(c => c.Property != null)
            .ToList();

        var rolesByCategory = new Dictionary<uint, HashSet<LootRole>>();

        foreach (var category in Plugin.DataManager.GetExcelSheet<ClassJobCategory>())
        {
            var roles = new HashSet<LootRole>();
            foreach (var (role, property) in columns)
            {
                if (roles.Contains(role))
                    continue;

                if (property!.GetValue(category) is true)
                    roles.Add(role);
            }

            if (roles.Count > 0)
                rolesByCategory[category.RowId] = roles;
        }

        Plugin.Log.Information($"Built job role index for {rolesByCategory.Count} job categories");
        return new JobRoleIndex(rolesByCategory);
    }

    /// <summary>A display heading plus a stable sort position for a piece's role set.</summary>
    public static (int Order, string Label) Describe(IReadOnlySet<LootRole> roles)
    {
        if (roles.Count == 0)
            return (99, "Anyone");

        if (roles.Count == 1)
        {
            var role = roles.First();
            return ((int)role, NameOf(role));
        }

        if (roles.Count == Enum.GetValues<LootRole>().Length)
            return (90, "Any role");

        // Mixed but not universal - accessories shared by a couple of roles, mostly.
        var ordered = roles.OrderBy(r => (int)r).Select(NameOf);
        return (80, string.Join(" / ", ordered));
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
