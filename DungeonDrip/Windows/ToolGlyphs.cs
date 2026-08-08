using Dalamud.Interface;
using DungeonDrip.Presentation;

namespace DungeonDrip.Windows;

/// <summary>The FontAwesome glyph behind each <see cref="ToolGlyph"/>.</summary>
/// <remarks>
/// These are the icons the panel toolbars have always shown, unchanged, and the readme's settings
/// prose describes them - so a change here is a change to a document as well as to a window.
///
/// <b>The native toolbar reads this table too, unlike <see cref="Glyphs"/>.</b> The marker column
/// is heading for curated game icons, where the two looks will honestly differ; a toolbar button is
/// a small monochrome shape on a button face, which the game has no vocabulary for and FontAwesome
/// does. So the native backend renders these same glyphs into textures rather than picking its own,
/// and the two toolbars cannot come to show different icons for the same filter. That is why this
/// sits apart from <see cref="Glyphs"/> despite looking like half of it.
/// </remarks>
internal static class ToolGlyphs
{
    public static FontAwesomeIcon For(ToolGlyph glyph) => glyph switch
    {
        ToolGlyph.ShowingOwned => FontAwesomeIcon.Eye,
        ToolGlyph.HidingOwned => FontAwesomeIcon.EyeSlash,

        ToolGlyph.CurrentJobOnly => FontAwesomeIcon.User,
        ToolGlyph.EveryJob => FontAwesomeIcon.Users,

        // A blade for the state where weapons are listed, and a bar through nothing for the state
        // where they are not: the pair reads as presence and absence rather than as two objects.
        ToolGlyph.WeaponsHidden => FontAwesomeIcon.Ban,
        ToolGlyph.WeaponsListed => FontAwesomeIcon.Khanda,

        ToolGlyph.ArmouryListed => FontAwesomeIcon.Archive,
        ToolGlyph.ArmouryHidden => FontAwesomeIcon.BoxOpen,

        ToolGlyph.EquippedListed => FontAwesomeIcon.Tshirt,
        ToolGlyph.EquippedHidden => FontAwesomeIcon.UserSlash,

        ToolGlyph.ShowingCollection => FontAwesomeIcon.BoxOpen,
        ToolGlyph.ShowingDuty => FontAwesomeIcon.Dungeon,

        ToolGlyph.PickerOpen => FontAwesomeIcon.SearchMinus,
        ToolGlyph.PickerShut => FontAwesomeIcon.Search,

        // The pin's three states are one button, and only two of them are pinned. The dice say where
        // letting go lands rather than what letting go does, which is the useful half.
        ToolGlyph.HoldingDuty => FontAwesomeIcon.Thumbtack,
        ToolGlyph.HoldingDutyOverRoulettes => FontAwesomeIcon.Dice,
        ToolGlyph.FollowingPlayer => FontAwesomeIcon.ThumbtackSlash,

        ToolGlyph.GroupedBySlot => FontAwesomeIcon.Vest,
        ToolGlyph.GroupedByRole => FontAwesomeIcon.PeopleGroup,
        ToolGlyph.GroupedBySource => FontAwesomeIcon.Skull,

        ToolGlyph.Settings => FontAwesomeIcon.Cog,

        ToolGlyph.SizeOverridden => FontAwesomeIcon.ArrowsAltV,

        _ => FontAwesomeIcon.Question,
    };
}
