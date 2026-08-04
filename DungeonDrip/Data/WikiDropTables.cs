using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DungeonDrip.Data;

/// <summary>
/// Reads a wiki page's drop tables: what is in them, and which boss or coffer each table sits under.
/// </summary>
/// <remarks>
/// Free of Dalamud and Lumina, like the ownership decision in Core and for the same reason - this is
/// a parser for third-party wikitext, the shape of which changes without warning, and it can be
/// reasoned about on its own. It also has to be: it runs on a worker thread, where touching a game
/// sheet is not allowed, so names arrive already resolved to ids in a dictionary.
/// </remarks>
internal static class WikiDropTables
{
    /// <summary>Names read off one page before the parse gives up on it.</summary>
    private const int MaxNamesPerPage = 2000;

    /// <summary>
    /// A section heading or a drop row, whichever comes next.
    /// </summary>
    /// <remarks>
    /// One pattern for both because the attribution is positional: a row belongs to the heading above
    /// it, and that is only knowable by walking the page in order. The backreference on the equals
    /// signs is what keeps the level straight, so a <c>====</c> coffer under a <c>===</c> heading is
    /// read as nested rather than as a sibling.
    /// </remarks>
    private static readonly Regex DropRowPattern = new(
        @"^(?<level>={2,6})[ \t]*(?<heading>.+?)[ \t]*\k<level>[ \t]*$" +
        @"|\{\{\s*Drops table row\s*\|\s*(?<item>[^|}\n]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    /// <summary>Wikitext furniture to take out of a heading before it is used as a label.</summary>
    private static readonly Regex HeadingNoise = new(
        @"\[\[\s*File\s*:.*?\]\]|\{\{.*?\}\}|'{2,}|<[^>]*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>A wiki link, keeping whatever it displays rather than what it points at.</summary>
    private static readonly Regex HeadingLink = new(
        @"\[\[(?:[^\]|]*\|)?([^\]|]+)\]\]", RegexOptions.Compiled);

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Headings that name the section rather than a source, so a row under one is unattributed.
    /// </summary>
    /// <remarks>
    /// Compared after cleaning and lowercasing. Everything else a loot table sits under is taken at
    /// face value: a heading this does not recognise is far more likely to be a boss whose name nobody
    /// anticipated than a container worth suppressing.
    /// </remarks>
    private static readonly HashSet<string> GenericHeadings = new(StringComparer.OrdinalIgnoreCase)
    {
        "loot", "drops", "rewards", "treasure coffers", "coffers", "items", "gear", "notes",
    };

    /// <summary>Longest a heading may be before it stops being a usable list heading.</summary>
    private const int MaxLabelLength = 48;

    /// <summary>Ceiling on distinct bosses and coffers taken off one page.</summary>
    private const int MaxAttributions = 40;

    /// <summary>
    /// Pulls item names out of the page's drop tables and resolves them to ids, recording which boss
    /// or coffer each table sat under. Storability is left to the merge step, which runs on the
    /// framework thread - nothing here may touch Lumina. Unresolvable names are counted, not guessed
    /// at.
    /// </summary>
    /// <remarks>
    /// The flat list is the answer that matters and is built exactly as it always was, from every drop
    /// row on the page. Attribution is layered over the same walk and is allowed to come out empty:
    /// what is on a duty's list must never depend on whether its page happens to be laid out with a
    /// heading per boss.
    /// </remarks>
    public static (uint[] Items, int Unmatched, LootAttribution[] Attributions) Parse(
        string wikitext, IReadOnlyDictionary<string, uint> names)
    {
        var resolved = new HashSet<uint>();
        var unmatched = 0;
        var seen = 0;

        // Innermost heading last, so a coffer nested under "Treasure Coffers" wins over the section
        // that contains it.
        var headings = new List<string>();
        var attributions = new Dictionary<string, LootAttribution>(StringComparer.OrdinalIgnoreCase);
        var attributed = new Dictionary<string, HashSet<uint>>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in DropRowPattern.Matches(wikitext))
        {
            var heading = match.Groups["heading"];
            if (heading.Success)
            {
                Descend(headings, match.Groups["level"].Value.Length, CleanHeading(heading.Value));
                continue;
            }

            if (++seen > MaxNamesPerPage)
                break;

            var name = match.Groups["item"].Value.Trim().Trim('[', ']').Trim();
            if (name.Length == 0)
                continue;

            if (!names.TryGetValue(name, out var itemId))
            {
                unmatched++;
                continue;
            }

            resolved.Add(itemId);

            var label = Innermost(headings);
            if (label == null)
                continue;

            if (!attributions.TryGetValue(label, out var attribution))
            {
                if (attributions.Count >= MaxAttributions)
                    continue;

                attributions[label] = attribution = new LootAttribution
                {
                    Label = label,
                    Order = attributions.Count,
                };

                attributed[label] = [];
            }

            attributed[attribution.Label].Add(itemId);
        }

        foreach (var (label, items) in attributed)
            attributions[label].Items = [.. items.Order()];

        return (
            [.. resolved.Order()],
            unmatched,
            [.. attributions.Values.Where(entry => entry.Items.Length > 0).OrderBy(entry => entry.Order)]);
    }

    /// <summary>
    /// Moves the heading stack to a new heading at <paramref name="level"/>.
    /// </summary>
    /// <remarks>
    /// The stack is indexed by depth rather than pushed and popped, because wiki pages skip levels -
    /// a page can go from <c>==</c> straight to <c>====</c> - and a strict pop would desynchronise on
    /// the first one that does. Anything deeper than the new heading is dropped, which is what makes
    /// a fresh boss heading forget the previous boss's subsections.
    /// </remarks>
    private static void Descend(List<string> headings, int level, string label)
    {
        // Level 2 is the page's own top level, so it lands at index 0.
        var depth = Math.Max(0, level - 2);

        if (headings.Count > depth)
            headings.RemoveRange(depth, headings.Count - depth);

        while (headings.Count < depth)
            headings.Add(string.Empty);

        headings.Add(label);
    }

    /// <summary>The deepest heading that names a source, or null when none of them do.</summary>
    private static string? Innermost(List<string> headings)
    {
        for (var i = headings.Count - 1; i >= 0; i--)
        {
            var heading = headings[i];
            if (heading.Length > 0 && !GenericHeadings.Contains(heading))
                return heading;
        }

        return null;
    }

    /// <summary>
    /// A heading reduced to the words in it.
    /// </summary>
    /// <remarks>
    /// Boss headings on these pages carry a difficulty or coffer image and wrap the name in a link -
    /// <c>[[File:Gold Coffer (small).png|32px|link=]] [[Octomammoth (boss)|Octomammoth]]</c> - so the
    /// image has to go and the link has to be reduced to what it displays rather than what it points
    /// at, which is where the "(boss)" disambiguators live. An over-long result is dropped rather than
    /// truncated: a heading that is really a sentence is not a list heading, and half of one is worse
    /// than none.
    /// </remarks>
    private static string CleanHeading(string raw)
    {
        var text = HeadingLink.Replace(HeadingNoise.Replace(raw, " "), "$1");
        text = Whitespace.Replace(text, " ").Trim(' ', '-', ':', ',');

        return text.Length is 0 or > MaxLabelLength ? string.Empty : text;
    }
}
