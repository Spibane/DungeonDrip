using System;

namespace DungeonDrip.Presentation;

/// <summary>One toolbar button: the state it is showing, what it says on hover, and what it does.</summary>
/// <remarks>
/// Not a <see cref="Node"/>, and that is deliberate. Everything else a panel shows is in one stream
/// that scrolls; a toolbar is the one part that must not, because the filters have to stay reachable
/// while the list under them is scrolled away. Describing it as a node would put it in the stream
/// and leave each backend to fish it back out again.
///
/// <paramref name="Press"/> carries the mutation rather than a flag reporting one, so nothing above
/// this has to thread a <c>changed</c> boolean back up through the buttons to whoever knows to save
/// the configuration. The presenter that built the button is the thing that knows what pressing it
/// has to invalidate, and this is how it says so once.
/// </remarks>
/// <param name="Enabled">
/// False for a button that is showing a state it cannot move from. Greyed rather than hidden, and
/// only where the button's absence would be read as the feature being missing - the duty view's pin
/// is the one case, since "following wherever you are" is a state worth seeing named.
/// </param>
internal sealed record ToolButtonModel(
    ToolGlyph Icon, string Tooltip, Action Press, bool Enabled = true);
