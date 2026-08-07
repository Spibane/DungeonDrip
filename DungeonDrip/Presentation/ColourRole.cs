namespace DungeonDrip.Presentation;

/// <summary>What a piece of text is saying, rather than what colour it comes out.</summary>
/// <remarks>
/// The six values were previously six <c>Vector4</c> constants, which was enough while ImGui was
/// the only thing that drew. It is not enough for a second backend: the greys and ambers were
/// picked to read on a dark ImGui window, and the game's own windows are whatever theme the player
/// chose. A backend has to be free to answer "muted" with reduced alpha and a full-strength
/// outline rather than with 0.58 grey, and it can only do that if what reaches it is the meaning.
///
/// So nothing above the backends names a colour. <see cref="DungeonDrip.Windows.Palette"/> resolves
/// these to the ImGui values they have always had.
/// </remarks>
internal enum ColourRole
{
    /// <summary>Ordinary text, in whatever the surface's usual colour is.</summary>
    Default,

    /// <summary>Something to act on or distrust: a stale snapshot, hidden weapons.</summary>
    Warning,

    /// <summary>Finished: a complete duty list, a completed outfit.</summary>
    Good,

    /// <summary>Present but not the point - owned pieces, secondary lines.</summary>
    Muted,

    /// <summary>Not collected, in the loot roll list.</summary>
    Missing,

    /// <summary>Not collected, at a vendor, where gold read as a recommendation to buy.</summary>
    NotOwned,

    /// <summary>The duty currently being looked at.</summary>
    Focus,
}
