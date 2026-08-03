namespace DungeonDrip;

/// <summary>
/// The settings every addon-anchored panel has, one object per panel.
/// </summary>
/// <remarks>
/// Flat properties per panel would be four near-identical fields each, and the naming is the least
/// of it: three panels' worth would be twelve places to write the wrong one.
/// </remarks>
public sealed class PanelSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Which side of the game window it rides on. Auto flips if space is tight.</summary>
    public PanelSide Side { get; set; } = PanelSide.Auto;

    public bool GroupBySlot { get; set; } = true;

    /// <summary>
    /// Size the user dragged it to. Zero means follow the addon's height and fit the width to the
    /// longest name, which is the default and what the reset button restores.
    /// </summary>
    public float Width { get; set; }

    /// <inheritdoc cref="Width"/>
    public float Height { get; set; }
}
