using System;
using System.Collections.Generic;
using Lumina.Excel.Sheets;

namespace DungeonDrip.Core;

/// <summary>
/// Item name -> row id, for turning the item names a wiki lists into ids.
/// </summary>
/// <remarks>
/// Built on the framework thread because it reads Lumina, then handed to background work as an
/// immutable map. Built lazily - it is only needed if the wiki source is actually used.
/// </remarks>
public static class ItemNameIndex
{
    private static IReadOnlyDictionary<string, uint>? cached;

    public static IReadOnlyDictionary<string, uint> Get()
    {
        if (cached != null)
            return cached;

        var map = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in Plugin.DataManager.GetExcelSheet<Item>())
        {
            var name = item.Name.ExtractText();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            // Names are not unique across the sheet; the lowest row id is the real item, later
            // duplicates are usually event or internal copies.
            map.TryAdd(name, item.RowId);
        }

        Plugin.Log.Information($"Built item name index: {map.Count} names");
        return cached = map;
    }
}
