using System;
using System.Collections.Generic;
using System.Linq;
using Lumina.Excel.Sheets;

namespace DungeonDrip.Core;

/// <summary>
/// Names for the gear the plugin knows a source for, so a piece can be looked up by typing it.
/// </summary>
/// <remarks>
/// Built from the drop index rather than from the whole Item sheet. That is a few thousand rows
/// instead of forty-odd thousand, and the restriction is the point rather than a saving: this is
/// the exact set of pieces the plugin can say anything useful about, so a miss is honestly "not
/// something I track" instead of a shrug about a potion.
///
/// <see cref="ItemNameIndex"/> covers everything and stays where it is, feeding the wiki parser,
/// which genuinely does have to resolve arbitrary names off a page.
/// </remarks>
public sealed class GearNameIndex
{
    private readonly Dictionary<string, uint> byName;
    private readonly List<(uint ItemId, string Name)> all;

    private GearNameIndex(Dictionary<string, uint> byName, List<(uint, string)> all)
    {
        this.byName = byName;
        this.all = all;
    }

    /// <summary>An exact name, which wins outright over any number of partial ones.</summary>
    public bool TryGetExact(string name, out uint itemId) =>
        byName.TryGetValue(name.Trim(), out itemId);

    /// <summary>Everything whose name contains the query, alphabetically, capped.</summary>
    public IReadOnlyList<(uint ItemId, string Name)> Search(string query, int limit)
    {
        var trimmed = query.Trim();
        if (trimmed.Length == 0)
            return [];

        return
        [
            .. all
                .Where(entry => entry.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
                .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .Take(limit),
        ];
    }

    public static GearNameIndex Build(IReadOnlyCollection<uint> itemIds)
    {
        var sheet = Plugin.DataManager.GetExcelSheet<Item>();
        var byName = new Dictionary<string, uint>(itemIds.Count, StringComparer.OrdinalIgnoreCase);
        var all = new List<(uint, string)>(itemIds.Count);

        foreach (var itemId in itemIds)
        {
            if (!sheet.TryGetRow(itemId, out var item))
                continue;

            var name = item.Name.ExtractText();
            if (name.Length == 0)
                continue;

            // Names are not unique across the sheet; first in wins, matching how the wiki index
            // resolves the same collision.
            byName.TryAdd(name, itemId);
            all.Add((itemId, name));
        }

        return new GearNameIndex(byName, all);
    }
}
