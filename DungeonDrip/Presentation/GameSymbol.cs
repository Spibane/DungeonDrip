namespace DungeonDrip.Presentation;

/// <summary>A mark from the game's own icon set, named by what it claims.</summary>
/// <remarks>
/// Separate from <see cref="GlyphToken"/> because the two are drawn by different machinery and
/// neither can do the other's job. A <see cref="GlyphToken"/> is a font glyph the plugin renders
/// itself, which is how a marker column can say "in the Armoire" and "worn by one of your
/// retainers" - the game has no icons for those. These are the game's own icons, which it draws
/// from a payload in the text: a much smaller vocabulary of stars, diamonds and crosses, and the
/// only marks that look like they belong to the game rather than to a plugin.
///
/// So this stays small on purpose. A state that needs saying precisely wants a
/// <see cref="GlyphToken"/>; a state that only needs pointing at wants one of these.
/// </remarks>
internal enum GameSymbol
{
    /// <summary>Not collected, on a list where that is the reason to act.</summary>
    Wanted,
}
