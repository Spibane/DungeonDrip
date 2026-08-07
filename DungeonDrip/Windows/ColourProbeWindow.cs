using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using KamiToolKit.Classes;

namespace DungeonDrip.Windows;

/// <summary>
/// A grid of the game's own themed colours, for finding the two the native panels should use.
/// </summary>
/// <remarks>
/// <b>Temporary, and meant to be deleted.</b> It exists to answer one question the native backend
/// is stuck on: which colour holder ids are ordinary text and its outline. Those ids are the fix
/// for two separate things - text that is unreadable on the light themes, and tooltip caveats that
/// cannot be coloured - and guessing them has already been tried and got white on white.
///
/// The holder is themed, so the same id is a different colour under each of the four themes. That
/// is the whole point of using it and also why this cannot be answered by reading a sheet: the
/// right id is the one that comes out dark on parchment and pale on black, which takes an eye and
/// two screenshots.
///
/// Deliberately an ImGui window rather than a native one. A native window drawn in the colours
/// being judged would be judging itself.
/// </remarks>
internal sealed class ColourProbeWindow : Window
{
    /// <summary>
    /// How many ids to show.
    /// </summary>
    /// <remarks>
    /// Modest on purpose. The holder is read through a pointer and an id past the end of it is not
    /// a blank swatch but a read into whatever follows, so this walks up rather than starting at a
    /// number that sounds generous.
    /// </remarks>
    private const uint Count = 64;

    private const int PerRow = 8;

    public ColourProbeWindow()
        : base("Dungeon Drip - colour probe###DungeonDripColourProbe")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420f, 300f),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void Draw()
    {
        ImGui.TextWrapped(
            "Each swatch is one colour holder id, as the game resolves it under the theme you are " +
            "using now. Wanted: the id that is dark on the light themes and pale on the dark ones - " +
            "that is ordinary text - and its opposite, which is the outline.");

        ImGui.Spacing();
        ImGui.TextColored(Palette.For(Presentation.ColourRole.Muted),
            "Switch theme in Character Configuration and compare. Screenshot both.");

        ImGui.Separator();
        ImGui.Spacing();

        var swatch = new Vector2(40f, 40f);

        for (var id = 0u; id < Count; id++)
        {
            if (id % PerRow != 0)
                ImGui.SameLine();

            using var group = Dalamud.Interface.Utility.Raii.ImRaii.Group();

            var colour = ColorHelper.GetColor(id);

            ImGui.ColorButton($"##colour{id}", colour, ImGuiColorEditFlags.AlphaPreview, swatch);
            ImGui.Text($"{id,3}");
        }
    }
}
