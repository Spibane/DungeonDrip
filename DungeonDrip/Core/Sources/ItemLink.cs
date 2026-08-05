using System;

namespace DungeonDrip.Core.Sources;

/// <summary>
/// Builds a reference-site URL for an item, for the sites a player might prefer.
/// </summary>
/// <remarks>
/// String assembly only - no request is made, so this works with the network down and cannot fail.
/// That is also its limit: it produces a plausible URL, never a checked one.
///
/// <para><b>The two keying schemes fail differently, and callers should know which they picked.</b>
/// An id-keyed site is exact - the page exists if the item does. A name-keyed one is a *guess* at an
/// article title, so a piece whose article is titled differently, or has none, lands on the site's
/// "no such page" screen. That is why the default is id-keyed. It is an acceptable outcome rather
/// than a bug, and nothing here should grow a lookup table of exceptions to paper over it.</para>
/// </remarks>
public static class ItemLink
{
    /// <summary>
    /// Shared with <see cref="Data.WikiLootSource"/>, which reads drop tables off the same wiki
    /// through its API. One constant so the two cannot drift onto different hosts.
    /// </summary>
    public const string ConsoleGamesWikiHost = "https://ffxiv.consolegameswiki.com";

    /// <summary>The URL for one piece on the configured site.</summary>
    /// <remarks>
    /// <paramref name="itemName"/> is ignored by the id-keyed sites and <paramref name="itemId"/> by
    /// the name-keyed ones, rather than the signature being split in two: every caller has both to
    /// hand, and a split would push the choice of which to pass out to each surface.
    /// </remarks>
    public static string For(LookupSite site, uint itemId, string itemName) => site switch
    {
        LookupSite.Teamcraft => $"https://ffxivteamcraft.com/db/en/item/{itemId}",
        LookupSite.GarlandTools => $"https://www.garlandtools.org/db/#item/{itemId}",
        LookupSite.Universalis => $"https://universalis.app/market/{itemId}",
        LookupSite.ConsoleGamesWiki => $"{ConsoleGamesWikiHost}/wiki/{ArticleTitle(itemName)}",
        LookupSite.GamerEscape => $"https://ffxiv.gamerescape.com/wiki/{ArticleTitle(itemName)}",

        // The only one that cannot address an item at all: it takes a search and lands on a results
        // list, so the player finishes the last step by hand.
        LookupSite.Lodestone =>
            "https://na.finalfantasyxiv.com/lodestone/playguide/db/item/" +
            $"?db_search_category=item&q={Uri.EscapeDataString(itemName)}",

        _ => $"https://ffxivteamcraft.com/db/en/item/{itemId}",
    };

    /// <summary>
    /// The site's bare name, which is the whole of what a link is labelled with.
    /// </summary>
    /// <remarks>
    /// No article and no verb. These labels used to read "Look it up on the Console Games Wiki", which
    /// spent five words explaining a convention anyone running plugins already knows - a coloured,
    /// hoverable name in chat is a link. The name alone also fits a menu row without wrapping.
    /// </remarks>
    public static string NameOf(LookupSite site) => site switch
    {
        LookupSite.Teamcraft => "Teamcraft",
        LookupSite.GarlandTools => "Garland Tools",
        LookupSite.Universalis => "Universalis",
        LookupSite.ConsoleGamesWiki => "Console Games Wiki",
        LookupSite.GamerEscape => "Gamer Escape",
        LookupSite.Lodestone => "Lodestone",
        _ => "Teamcraft",
    };

    /// <summary>
    /// An item name as a MediaWiki article title.
    /// </summary>
    /// <remarks>
    /// Underscores are substituted before escaping, not after: a space escapes to <c>%20</c>, which
    /// both wikis accept but which produces an ugly and non-canonical URL, whereas an underscore is
    /// the form the sites themselves link with. Everything else is escaped, because item names contain
    /// apostrophes and ampersands that would otherwise cut the path short.
    /// </remarks>
    private static string ArticleTitle(string itemName) =>
        Uri.EscapeDataString(itemName.Trim().Replace(' ', '_'));
}
