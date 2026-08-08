using Dalamud.Game.Text.SeStringHandling;
using DungeonDrip.Presentation;

namespace DungeonDrip.Windows;

/// <summary>The game icon behind each <see cref="GameSymbol"/>.</summary>
/// <remarks>
/// One table for both looks, like <see cref="ToolGlyphs"/> and unlike <see cref="Glyphs"/>: these
/// are the game's own icons, so there is no second answer either backend could give.
///
/// <see cref="BitmapFontIcon.NoCircle"/> is what <c>ItemTooltipLine</c> already puts on a piece that
/// is not collected, and using it again here is the point rather than a coincidence. The plugin was
/// saying that with a red cross on a panel and a no-circle in a tooltip, which is two vocabularies
/// for one fact; where the game's own icons are usable, its vocabulary is the one to speak.
/// </remarks>
internal static class GameSymbols
{
    public static BitmapFontIcon For(GameSymbol symbol) => symbol switch
    {
        GameSymbol.Wanted => BitmapFontIcon.NoCircle,
        _ => BitmapFontIcon.None,
    };
}
