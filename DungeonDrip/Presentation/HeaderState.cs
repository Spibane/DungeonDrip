namespace DungeonDrip.Presentation;

/// <summary>Which collapsible headings the user has folded, and remembering it across restarts.</summary>
/// <remarks>
/// Three surfaces wrote this out separately - the duty list, the addon panels and the collection
/// view - and the third had grown a second rule the other two had not: a heading that starts shut
/// records being opened rather than recording not being shut. Two of the three could not have told
/// anyone that, because they only ever had headings that start open.
///
/// Which config list a deviation goes in is the whole subtlety, and it is not a drawing decision, so
/// it is here rather than in a backend. The drawing half stays with each backend, because a folded
/// heading is an ImGui <c>CollapsingHeader</c> in one and a node in the other.
/// </remarks>
internal sealed class HeaderState(Configuration configuration)
{
    /// <summary>Whether a heading should come up open.</summary>
    /// <param name="key">
    /// Stable across redraws, and deliberately not the label: a label carrying a count changes as
    /// gear moves, and the folded state has to outlive that.
    /// </param>
    /// <param name="defaultOpen">
    /// A parameter rather than something seeded into the collapsed list at startup. Seeding writes
    /// the config as a side effect of drawing, and worse, it cannot tell a heading nobody has
    /// touched from one the user deliberately opened - so the next launch would close it again.
    /// </param>
    public bool IsOpen(string key, bool defaultOpen = true) => defaultOpen
        ? !configuration.CollapsedGroups.Contains(key)
        : configuration.OpenedGroups.Contains(key);

    /// <summary>
    /// Writes back whatever the heading settled on, and saves only when something moved.
    /// </summary>
    /// <remarks>
    /// Which list records the deviation depends on which way the default points, so a heading that
    /// starts shut remembers being opened rather than remembering not being shut. Recording both in
    /// one list would make "absent" mean open on some headings and shut on others.
    /// </remarks>
    public void Record(string key, bool open, bool defaultOpen = true)
    {
        if (open == IsOpen(key, defaultOpen))
            return;

        var remembering = defaultOpen ? configuration.CollapsedGroups : configuration.OpenedGroups;

        if (open == defaultOpen)
            remembering.Remove(key);
        else
            remembering.Add(key);

        configuration.Save();
    }
}
