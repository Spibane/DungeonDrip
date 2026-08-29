using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures.TextureWraps;
using KamiToolKit.Nodes;

namespace DungeonDrip.Native;

/// <summary>Renders a FontAwesome glyph into a texture a game node can show, once each.</summary>
/// <remarks>
/// This exists so the marks are the same in both looks. The alternative was a second table of
/// curated game icon ids, and the game has no vocabulary for "showing pieces you already have" or
/// "worn by one of your retainers" - the icons it ships are objects and actions, not claims about a
/// wardrobe - so that table would have been guesses, and the two looks would have drifted the first
/// time one of them was corrected.
///
/// <b>The rendering has to happen inside an ImGui frame, which is why this is a service and not a
/// function.</b> Nothing on the native side is in a frame: panels are built on the framework tick,
/// where there is no draw list to draw a glyph into. So a glyph that has not been drawn yet cannot
/// be handed over on the spot, and <see cref="TryLoad"/> says so rather than pretending.
///
/// <b>The glyph goes into the foreground draw list under a clip rectangle parked off-screen, and
/// the obvious alternative does not work.</b> Drawing into an off-screen ImGui window was tried
/// first and produced transparent textures every time: a window's inner clip rectangle is
/// intersected with the viewport when it is opened, so a window nobody can see clips to nothing and
/// every glyph is culled before it reaches a draw list. A clip rectangle pushed with no
/// intersection is not treated that way, so the text survives - and the game still draws nothing,
/// because the scissor it ends up with is off the screen.
///
/// Capturing the whole foreground list is not the waste it looks. Anything else drawn into it that
/// frame sits at ordinary screen coordinates, which land far outside a glyph-sized texture once the
/// same offset is taken off them, so it is scissored away rather than composited.
/// </remarks>
internal sealed class GlyphTextures : IDisposable
{
    /// <summary>
    /// Where the first glyph of a frame is drawn, which is nowhere.
    /// </summary>
    /// <remarks>
    /// Far enough out that no screen or scissor rectangle can reach it. It only has to be a place
    /// the clip rectangle can be put without overlapping anything real.
    /// </remarks>
    private static readonly Vector2 OffScreen = new(-8000f, -8000f);

    /// <summary>
    /// How far apart two glyphs rendered in the same frame are put.
    /// </summary>
    /// <remarks>
    /// <b>Not decoration - without it every glyph but the last carries the ones drawn after it.</b>
    /// The draw list is not emptied between requests, so a second glyph drawn where the first was
    /// is captured together with it. Given each its own place, every other glyph in the list lands
    /// outside a glyph-sized texture once the offset comes off, and is scissored away.
    ///
    /// Wider than any glyph could be, so two can never reach each other.
    /// </remarks>
    private const float SlotStride = 256f;

    /// <summary>
    /// A pixel of slack around the measured glyph, against rounding and nothing else.
    /// </summary>
    /// <remarks>
    /// The bounds below are exact, so this is not the margin an earlier cut used to guess an
    /// overhang with - that one was three pixels on every side of a seventeen-pixel mark, which is
    /// most of why the icons came out small. This is one, and only because the bounds are floats
    /// and the texture is whole pixels.
    /// </remarks>
    private const float Slack = 1f;

    /// <summary>
    /// One texture per glyph and colour, kept for the session and never given to a node.
    /// </summary>
    /// <remarks>
    /// The cache is what makes this affordable on a list rather than only on a toolbar. Sixty rows
    /// carry at most a dozen distinct marks between them, and rendering per row would mean sixty
    /// textures built and freed on every rebuild.
    ///
    /// Keyed on the colour as well as the glyph because the colour is drawn in rather than tinted
    /// on. That is a handful of entries, not a multiplication of them - a given mark appears in one
    /// or two roles - and it self-corrects when the player changes theme, since the two roles that
    /// follow the theme resolve to different colours and so to different entries.
    ///
    /// What a node gets is a new reference to one of these rather than the thing itself:
    /// <c>ImGuiImageNode.LoadTexture</c> takes ownership of what it is handed and disposes it, so
    /// handing over a master would free it out from under every other node showing the same mark.
    /// </remarks>
    private readonly Dictionary<(FontAwesomeIcon Icon, uint Colour), IDrawListTextureWrap> rendered = [];

    /// <summary>Glyphs asked for and not yet drawn. A set, so twenty rows asking cost one render.</summary>
    private readonly HashSet<(FontAwesomeIcon Icon, uint Colour)> pending = [];

    /// <summary>Names one texture apart from another in the log, and nothing more.</summary>
    private int sequence;

    /// <summary>
    /// Packs a colour the way a draw list wants it, which is not the order it is written in.
    /// </summary>
    /// <remarks>
    /// Alpha, blue, green, red - the reverse of how the rest of the plugin says a colour. Getting
    /// this wrong is silent: every channel is still a channel, so the mark appears and is simply the
    /// wrong colour, which is a harder thing to notice than a mark that does not appear.
    /// </remarks>
    private static uint Pack(Vector4 colour)
    {
        static uint Channel(float value) => (uint)(Math.Clamp(value, 0f, 1f) * 255f);

        return (Channel(colour.W) << 24) | (Channel(colour.Z) << 16) |
               (Channel(colour.Y) << 8) | Channel(colour.X);
    }
    /// <summary>
    /// Raised on the frame a glyph that was asked for becomes available.
    /// </summary>
    /// <remarks>
    /// What a surface does with this is rebuild, which is the whole reason it is an event rather
    /// than a callback carrying a node. A callback would have to be told when its node stopped
    /// existing - and a panel closes, a list refilters and a filter is toggled while a texture is
    /// in flight - where a rebuild simply asks again for whatever is on screen now.
    /// </remarks>
    public event Action? Ready;

    public GlyphTextures()
    {
        Plugin.PluginInterface.UiBuilder.Draw += OnDraw;
    }

    /// <summary>
    /// Puts a glyph on a node if it has been drawn, and asks for it if it has not.
    /// </summary>
    /// <returns>
    /// Whether the node now has its mark. False means the glyph is being drawn and
    /// <see cref="Ready"/> will say when - not that anything went wrong.
    /// </returns>
    /// <remarks>
    /// The node is not remembered. Everything this touches, it touches now, which is what lets a
    /// row be freed the instant after without anything having to be told.
    /// </remarks>
    public bool TryLoad(ImGuiImageNode node, FontAwesomeIcon icon, Vector4 colour)
    {
        var key = (icon, Pack(colour));

        if (!rendered.TryGetValue(key, out var master))
        {
            pending.Add(key);
            return false;
        }

        var share = master.CreateWrapSharingLowLevelResource();

        // Before the texture goes in, and needed: the node crops to its texture size rather than
        // scaling to it, and the default is 32 where a glyph is whatever the icon font's size is. A
        // wrong size here shows a corner of the mark blown up to fill the node.
        node.TextureSize = share.Size;
        node.LoadTexture(share);
        return true;
    }

    /// <remarks>
    /// The slot counter rises whether or not a glyph was drawn, so no two glyphs drawn in one frame
    /// can share a place even when one of them is retried.
    /// </remarks>
    private void OnDraw()
    {
        if (pending.Count == 0)
            return;

        var slot = 0;
        var drawn = false;

        foreach (var key in new List<(FontAwesomeIcon, uint)>(pending))
        {
            if (!Render(key, slot++))
                continue;

            pending.Remove(key);
            drawn = true;
        }

        if (drawn)
            Ready?.Invoke();
    }

    /// <summary>
    /// Draws one glyph and keeps its texture.
    /// </summary>
    /// <returns>
    /// Whether the glyph is finished with. False means the icon font had nothing to measure yet and
    /// it should be left where it is - not that anything went wrong.
    /// </returns>
    /// <remarks>
    /// Guarded because this runs from the draw callback, where an exception is thrown once per frame
    /// forever rather than once. A mark that never appears is a poor mark; a plugin filling the log
    /// at sixty lines a second is a broken one, so a glyph that throws is given up on rather than
    /// retried.
    /// </remarks>
    private unsafe bool Render((FontAwesomeIcon Icon, uint Colour) key, int slot)
    {
        IDrawListTextureWrap? texture = null;

        try
        {
            var glyph = key.Icon.ToIconString();

            using var handle = Plugin.PluginInterface.UiBuilder.IconFontHandle.Push();

            // Rendered at the font's own size rather than at the node's. The glyphs are rasterised
            // once at that size, so asking for a larger one only blurs what is there.
            var font = ImGui.GetFont();
            var fontSize = ImGui.GetFontSize();

            if (fontSize < 1f)
                return false;

            // Where the ink actually is, rather than how far the pen moves. The two are not the same
            // - a glyph can begin left of where it is placed, which is what shaved the left edge off
            // the icons when the advance width was used as the texture's - and the difference is
            // held in the glyph rather than reported by any measuring call.
            var scale = fontSize / font.FontSize;
            var bounds = font.FindGlyph((char)key.Icon);
            if (bounds is null)
                return false;

            var ink = new Vector2(bounds->X1 - bounds->X0, bounds->Y1 - bounds->Y0) * scale;
            if (ink.X < 1f || ink.Y < 1f)
                return false;

            // Square, and the mark centred in it. A node stretches a texture to fill it rather than
            // fitting it, so a texture the shape of the glyph would be pulled out of shape by a
            // square node - and a square texture wastes nothing, because what fills it is the
            // longer of the glyph's two sides.
            var side = MathF.Ceiling(MathF.Max(ink.X, ink.Y)) + (Slack * 2f);
            var size = new Vector2(side, side);

            var drawList = ImGui.GetForegroundDrawList();
            var origin = OffScreen + new Vector2(slot * SlotStride, 0f);

            // Back off the glyph's own offset, so what lands centred is the mark and not the box it
            // is nominally drawn in.
            var pen = origin + ((size - ink) / 2f) - (new Vector2(bounds->X0, bounds->Y0) * scale);

            drawList.PushClipRect(origin, origin + size, false);
            drawList.AddText(font, fontSize, pen, key.Colour, glyph);
            drawList.PopClipRect();

            texture = Plugin.TextureProvider.CreateDrawListTexture($"DungeonDrip glyph {sequence++}");
            texture.Size = size;
            texture.Draw(drawList, origin, Vector2.One);

            rendered[key] = texture;
            return true;
        }
        catch (Exception exception)
        {
            Plugin.Log.Error(exception, $"Could not render {key.Icon} for a native surface.");
            texture?.Dispose();
            return true;
        }
    }

    public void Dispose()
    {
        Plugin.PluginInterface.UiBuilder.Draw -= OnDraw;

        foreach (var texture in rendered.Values)
            texture.Dispose();

        rendered.Clear();
        pending.Clear();
    }
}
