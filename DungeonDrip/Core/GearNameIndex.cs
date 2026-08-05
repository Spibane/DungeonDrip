using System;
using System.Collections.Generic;
using System.Linq;
using Lumina.Excel.Sheets;

namespace DungeonDrip.Core;

/// <summary>
/// Names for every piece of gear that can be kept, so a piece can be looked up by typing it.
/// </summary>
/// <remarks>
/// Built from the whole Item sheet filtered by <see cref="StorageEligibility"/>, which is close to
/// 29,000 rows. It used to be built from the drop index instead - a few thousand - and the
/// narrowness was defended as honesty: a miss meant "not something I track". That stopped being true
/// once the plugin could say where a piece comes from besides a duty, because the pieces the drop index
/// left out are exactly the crafted and bought ones there is now an answer for. Naming them was the
/// prerequisite for asking about them.
///
/// The filter is still the storage test rather than nothing at all, so a search cannot land on a potion.
/// It also no longer depends on the loot download, so a piece can be named on the first frame after
/// login and with the network down.
///
/// <see cref="ItemNameIndex"/> covers literally everything and stays where it is, feeding the wiki
/// parser, which genuinely does have to resolve arbitrary names off a page.
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

    /// <summary>
    /// The name of a piece already resolved to an id, for a caller that needs to phrase it.
    /// </summary>
    /// <remarks>
    /// A linear walk rather than a second dictionary. This is called once per chat answer, so a
    /// reverse index would be twenty thousand entries kept warm to save a walk nothing does in a loop.
    /// False for an id outside the storable set, which no caller here can produce but which should not
    /// return a wrong name if one ever did.
    /// </remarks>
    public bool TryGetName(uint itemId, out string name)
    {
        foreach (var (candidateId, candidateName) in all)
        {
            if (candidateId != itemId)
                continue;

            name = candidateName;
            return true;
        }

        name = string.Empty;
        return false;
    }

    /// <summary>
    /// Everything matching the query, best matches first, capped.
    /// </summary>
    /// <remarks>
    /// Ranked in three tiers - exact, then begins-with, then contains - and only alphabetical inside a
    /// tier. A plain alphabetical <c>Contains</c> was enough while this held a few thousand drop-only
    /// names and is useless over 29,000: "ring" matches 2,770 pieces, of which the 15 actually called
    /// "Ring of ..." would sort wherever the alphabet put them and fall off the end of the cap. The
    /// tiers are what put those 15 first.
    ///
    /// The cap is applied after ranking, not during, so a better match found late still displaces a
    /// worse one found early.
    /// </remarks>
    /// <returns>
    /// The best <paramref name="limit"/> matches, and how many there were in total. The total is not
    /// the list's length: it is what the over-cap message reports, and "more than 8 match" was a poor
    /// substitute for "2,770 match" when deciding whether to type more or give up.
    /// </returns>
    public (IReadOnlyList<(uint ItemId, string Name)> Shown, int Total) Search(string query, int limit)
    {
        var trimmed = query.Trim();
        if (trimmed.Length == 0)
            return ([], 0);

        var ranked = all
            .Select(entry => (Entry: entry, Tier: TierOf(entry.Name, trimmed)))
            .Where(entry => entry.Tier < NoMatch)
            .ToList();

        var shown = ranked
            .OrderBy(entry => entry.Tier)
            .ThenBy(entry => entry.Entry.Name, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(entry => entry.Entry)
            .ToList();

        return (shown, ranked.Count);
    }

    /// <summary>
    /// How well one name answers the query. Lower is better; <see cref="NoMatch"/> means it does not.
    /// </summary>
    private static int TierOf(string name, string query)
    {
        if (name.Equals(query, StringComparison.OrdinalIgnoreCase))
            return 0;

        if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            return 1;

        return name.Contains(query, StringComparison.OrdinalIgnoreCase) ? 2 : NoMatch;
    }

    /// <summary>Sorts past every real tier, so the filter can be a comparison rather than a second test.</summary>
    private const int NoMatch = 3;

    /// <summary>
    /// The single best match for a query, when there is an unambiguous one.
    /// </summary>
    /// <remarks>
    /// An exact name wins, then a sole begins-with match. The second rule is what makes a query like
    /// "Lunar Envoy's Jacket" resolve instead of listing the eleven pieces that contain it: a name the
    /// query is a prefix of is what someone typing a partial name almost always means, and if two of
    /// those exist the query really is ambiguous.
    ///
    /// Returns false rather than guessing when the only matches are mid-name, leaving the caller to
    /// list them.
    /// </remarks>
    public bool TryResolve(string query, out uint itemId)
    {
        if (TryGetExact(query, out itemId))
            return true;

        var trimmed = query.Trim();
        itemId = 0;

        if (trimmed.Length == 0)
            return false;

        var prefixed = 0;
        foreach (var (candidateId, name) in all)
        {
            if (!name.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase))
                continue;

            if (++prefixed > 1)
                return false;

            itemId = candidateId;
        }

        return prefixed == 1;
    }

    /// <summary>
    /// Every storable piece in the game, by name.
    /// </summary>
    /// <remarks>
    /// One sweep of the Item sheet on the framework thread. Called from the plugin's constructor rather
    /// than on a dataset revision, because nothing it reads changes while the plugin is loaded.
    /// </remarks>
    public static GearNameIndex BuildAll(StorageEligibility storage)
    {
        var byName = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        var all = new List<(uint, string)>();

        foreach (var item in Plugin.DataManager.GetExcelSheet<Item>())
        {
            if (storage.Of(item) == StorageKind.None)
                continue;

            var name = item.Name.ExtractText();
            if (name.Length == 0)
                continue;

            // First in wins on a collision, matching how the wiki index resolves the same tie. As
            // measured against the retail sheet no storable piece shares a name with another - 28,962
            // names, 28,962 distinct - so this is guarding a case that does not currently arise rather
            // than one that does. Kept because the sheet as a whole does contain duplicate names and
            // nothing stops a patch adding one inside the storable set.
            byName.TryAdd(name, item.RowId);
            all.Add((item.RowId, name));
        }

        Plugin.Log.Information($"Indexed {all.Count} storable gear names");
        return new GearNameIndex(byName, all);
    }
}
