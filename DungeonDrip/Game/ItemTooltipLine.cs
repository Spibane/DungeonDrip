using System;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Hooking;
using Dalamud.Memory;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using DungeonDrip.Core;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace DungeonDrip.Game;

/// <summary>
/// Adds one line to the game's own item tooltip saying whether the piece is in your collection.
/// </summary>
/// <remarks>
/// The only place the plugin modifies a game window's contents rather than riding beside it, and the
/// only place it hooks a game function. Both are off by default and both are why this file is so
/// careful.
///
/// <para><b>Why a hook rather than an addon event.</b> Dalamud's RequestedUpdate events fire at a
/// point where the tooltip's fields are not yet what the game will draw. Its own tooltip generator
/// is that moment, so that is where this sits. The technique, the signature, the field indices and
/// the multi-line flag all come from Simple Tweaks (Caraxi, AGPL-3.0), which has been doing this
/// for years - the useful parts were learned by reading it after guessing wrong twice.</para>
///
/// <para><b>Why the category row.</b> Two other homes were tried and neither works. The description
/// row is only written when the item has a description of its own and only drawn when the game
/// decided there was one, so a line put there for plain gear is accepted, kept, and never shown.
/// The extractable row at the top of the bottom block is drawn, but the tooltip's rows sit at fixed
/// positions - giving one a second line makes it draw over the row beneath rather than pushing it
/// down, and that is not something a string write can fix. The plugins that do get their own lines
/// down there build their own nodes for it.</para>
///
/// <para>So: the category row, appended inline, and kept to one of the game's own icons plus a
/// single word. That row is written for every item and always drawn, but it is not wide - which is
/// the reason for the shorthand rather than the sentences the panels use. Nothing is ever put here
/// that varies in length, so it cannot outgrow the space.</para>
///
/// <para>The signature will eventually break on a patch. When it does the hook simply fails to
/// install, the feature is absent, and everything else carries on - which is the right failure for
/// a cosmetic line.</para>
/// </remarks>
public sealed unsafe class ItemTooltipLine : IDisposable
{
    /// <summary>
    /// <c>GenerateItemTooltip(AtkUnitBase* addon, NumberArrayData*, StringArrayData*)</c>.
    /// </summary>
    private const string Signature =
        "48 89 5C 24 ?? 55 56 57 41 54 41 55 41 56 41 57 48 83 EC ?? 48 8B 42 ?? 4C 8B EA";

    /// <summary>
    /// The tooltip's category line - "Body", "Hands", "Miscellany".
    /// </summary>
    /// <remarks>
    /// Not the description field, which was the obvious choice and does not work. The game only
    /// writes that one when the item has a description of its own, and only draws it when it
    /// decided there was one - so on the majority of gear a line written there is accepted,
    /// retained, and never shown. The category line is written for every item and always drawn.
    /// </remarks>
    private const int ItemUiCategory = 2;

    /// <summary>
    /// The state, as one of the game's own tooltip icons.
    /// </summary>
    /// <remarks>
    /// Game icons rather than characters, so the line looks like part of the tooltip instead of
    /// something pasted into it, and so it survives whatever font the player is on. Kept close to
    /// what the panels already say - a star means a finished outfit and nothing else, and the one
    /// state worth acting on is the one that stands out.
    ///
    /// This lives here rather than beside the marker vocabulary in Core, which is deliberately free
    /// of anything Dalamud.
    /// </remarks>
    private static BitmapFontIcon Glyph(CollectionMarker marker) => marker switch
    {
        CollectionMarker.Dresser => BitmapFontIcon.GreenDot,
        CollectionMarker.Armoire => BitmapFontIcon.GreenDot,
        CollectionMarker.Outfit => BitmapFontIcon.SilverStar,
        CollectionMarker.OutfitComplete => BitmapFontIcon.GoldStar,
        CollectionMarker.Inventory => BitmapFontIcon.OrangeDiamond,
        _ => BitmapFontIcon.NoCircle,
    };

    /// <summary>
    /// Where it is, in one word. The icon has already said whether you have it.
    /// </summary>
    /// <remarks>
    /// Short because this shares a row with the item's category and that row is not wide. The full
    /// sentences the panels use do not fit and do not need to: there are six states and the icon
    /// carries most of the meaning.
    /// </remarks>
    private static string Word(CollectionMarker marker, bool stale) => marker switch
    {
        CollectionMarker.Dresser => "Dresser",
        CollectionMarker.Armoire => "Armoire",
        CollectionMarker.Outfit => "Outfit",
        CollectionMarker.OutfitComplete => "Outfit",
        CollectionMarker.Inventory => "Carried",

        // The one worth acting on, and the one an old snapshot can be wrong about, so it is the
        // only one that spends characters on a caveat.
        _ => stale ? "Not owned?" : "Not owned",
    };

    /// <summary>Ours, so the line can recognise itself and never double up.</summary>
    private const uint MarkerCommandId = 0x44445F31;

    private delegate void* GenerateItemTooltipDelegate(
        AtkUnitBase* addon, NumberArrayData* numbers, StringArrayData* strings);

    private readonly Plugin plugin;
    private readonly Hook<GenerateItemTooltipDelegate>? hook;
    private readonly DalamudLinkPayload marker;

    private bool warned;

    // TEMPORARY: reports which guard the line is stopping at, toggled by "/dungeondrip tooltipdebug".
    private bool debug;
    private uint debugLast;
    private bool firedOnce;

    public string ToggleDebug()
    {
        debug = !debug;
        debugLast = 0;
        firedOnce = false;
        if (debug && hook == null)
            return "Dungeon Drip: the tooltip hook never installed - the signature did not match.";

        return debug
            ? "Dungeon Drip: tooltip diagnostics ON. Hover a few pieces, then check /xllog."
            : "Dungeon Drip: tooltip diagnostics OFF.";
    }

    private bool Trace(uint itemId, string outcome)
    {
        if (debug && itemId != debugLast)
        {
            debugLast = itemId;
            Plugin.Log.Information($"[tooltip] {itemId}: {outcome}");
        }

        return false;
    }

    public ItemTooltipLine(Plugin plugin, ISigScanner scanner, IGameInteropProvider interop)
    {
        this.plugin = plugin;
        marker = Plugin.ChatGui.AddChatLinkHandler(MarkerCommandId, (_, _) => { });

        try
        {
            var address = scanner.ScanText(Signature);
            hook = interop.HookFromAddress<GenerateItemTooltipDelegate>(address, Detour);
            hook.Enable();
        }
        catch (Exception ex)
        {
            // A patch moved the function. The tooltip line is simply unavailable until the
            // signature is updated; nothing else in the plugin depends on it.
            Plugin.Log.Warning($"Item tooltip line unavailable - could not find the tooltip generator. {ex.Message}");
        }
    }

    public void Dispose()
    {
        hook?.Disable();
        hook?.Dispose();
        Plugin.ChatGui.RemoveChatLinkHandler(MarkerCommandId);
    }

    /// <summary>Whether the hook found its function, for Settings to say so rather than lie.</summary>
    public bool Available => hook != null;

    private void* Detour(AtkUnitBase* addon, NumberArrayData* numbers, StringArrayData* strings)
    {
        // Wrapped whole. Whatever happens in here, the game's own tooltip still gets generated -
        // an exception must never cost the user their tooltip.
        try
        {
            if (debug && !firedOnce)
            {
                firedOnce = true;
                Plugin.Log.Information(
                    $"[tooltip] detour is firing. setting={plugin.Configuration.ShowTooltipLine}");
            }

            if (plugin.Configuration.ShowTooltipLine)
                Append(strings);
        }
        catch (Exception ex)
        {
            if (!warned)
            {
                warned = true;
                Plugin.Log.Warning($"Item tooltip line failed and will stay quiet from here. {ex}");
            }
        }

        return hook!.Original(addon, numbers, strings);
    }

    private void Append(StringArrayData* strings)
    {
        if (strings == null || strings->StringArray == null || strings->Size <= ItemUiCategory)
        {
            Trace(uint.MaxValue, $"array unusable (size {(strings == null ? -1 : strings->Size)})");
            return;
        }

        var hovered = Plugin.GameGui.HoveredItem;
        if (hovered is <= 0 or > uint.MaxValue)
        {
            Trace(uint.MaxValue, $"HoveredItem is {hovered} at generate time");
            return;
        }

        var (itemId, kind) = ItemUtil.GetBaseId((uint)hovered);

        // Key items sit behind an offset sharing the id space with real gear, and a collectable is
        // a turn-in rather than a glamour decision. HQ is not excluded - HQ gear is real gear, and
        // the base id is what the dresser stores.
        if (itemId == 0 || kind is ItemKind.EventItem or ItemKind.Collectible)
        {
            Trace(itemId, $"kind {kind}");
            return;
        }

        // The same content rule as every other surface, so the absence of a line keeps meaning
        // "not glamour gear" rather than "you have it".
        if (!plugin.Storage.CanBeStored(itemId))
        {
            Trace(itemId, "not storable");
            return;
        }

        var source = MissingItems.Resolve(
            itemId,
            plugin.Ownership.Current,
            plugin.Outfits.SetsContaining(itemId),
            plugin.Configuration.OutfitOwnership,
            plugin.Configuration.Scope);

        var marker0 = CollectionMarkers.For(source, plugin.Ownership.HasDresserData);

        // A tooltip has no room to explain why it cannot say, so it says nothing.
        if (marker0 == CollectionMarker.Unknown)
        {
            Trace(itemId, "no dresser data");
            return;
        }

        var existing = Rebase(Read(strings, ItemUiCategory));

        var stale = plugin.Ownership.IsDresserStale;
        var colour = marker0 switch
        {
            CollectionMarker.NotCollected => stale ? (ushort)26 : (ushort)14,
            CollectionMarker.Inventory => (ushort)26,
            _ => (ushort)45,
        };

        // Appended, never replaced: whatever is already there - including a line another plugin
        // added in an earlier hook - is re-emitted first and ours lands after it.
        var built = new SeStringBuilder();
        foreach (var payload in existing.Payloads)
            built.Add(payload);

        // Attributed, because an unexplained line in a game tooltip is the kind of thing that gets
        // reported to Square Enix. Short, because this shares a line with the item's category.
        // Inline, and it has to stay inline: the tooltip's rows sit at fixed positions, so a row
        // given a second line draws over the row beneath it rather than pushing it down.
        built.Add(marker)
            .Add(RawPayload.LinkTerminator)
            .AddText("   ")
            .Add(new IconPayload(Glyph(marker0)))
            .AddUiForeground(colour)
            .AddText(Word(marker0, stale))
            .AddUiForegroundOff();

        var bytes = built.Build().EncodeWithNullTerminator();
        strings->SetValue(ItemUiCategory, bytes, false);

        Trace(itemId, $"WROTE {bytes.Length} bytes ({Word(marker0, stale)})");
    }

    /// <summary>
    /// The line as the game wrote it, with anything we added last time removed.
    /// </summary>
    /// <remarks>
    /// Truncating at our own marker rather than checking for its presence and giving up. That check
    /// was the first attempt and it deadlocked the feature: it was reading a field that goes stale,
    /// so after one write every later item looked already done. Rebuilding from the game's own text
    /// each time is idempotent whether the field is fresh or not, which is the property actually
    /// wanted. A line another plugin added survives, since it lands before our marker.
    /// </remarks>
    private static SeString Read(StringArrayData* strings, int field)
    {
        if (strings->Size <= field || strings->StringArray == null)
            return new SeString();

        var raw = strings->StringArray[field];
        return raw.Value == null
            ? new SeString()
            : MemoryHelper.ReadSeStringNullTerminated((nint)raw.Value);
    }

    private SeString Rebase(SeString present)
    {
        var kept = new SeString();

        foreach (var payload in present.Payloads)
        {
            if (payload is DalamudLinkPayload link &&
                link.CommandId == MarkerCommandId &&
                link.Plugin == Plugin.PluginInterface.InternalName)
            {
                break;
            }

            kept.Payloads.Add(payload);
        }

        return kept;
    }

}
