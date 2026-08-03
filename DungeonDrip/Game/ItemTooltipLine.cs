using System;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using Dalamud.Memory;
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
/// point where the tooltip's description field still holds the previous item's text whenever the
/// current item has none of its own - which is true of most gear. Measured before and after that
/// event and it is stale in both. The game's own tooltip generator is the moment the fields are
/// correct, so that is where this has to sit. The technique, the signature and the field index are
/// all taken from Simple Tweaks (Caraxi, AGPL-3.0), which has been doing this for years.</para>
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

    /// <summary>The description field of the tooltip's string array.</summary>
    private const int ItemDescription = 13;

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
        if (strings == null || strings->StringArray == null || strings->Size <= ItemDescription)
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

        var raw = strings->StringArray[ItemDescription];
        var present = raw.Value == null
            ? new SeString()
            : MemoryHelper.ReadSeStringNullTerminated((nint)raw.Value);

        var existing = Rebase(present, itemId);

        var stale = plugin.Ownership.IsDresserStale;
        var colour = marker0 switch
        {
            CollectionMarker.NotCollected => stale ? (ushort)26 : (ushort)14,
            CollectionMarker.Inventory => (ushort)26,
            _ => (ushort)45,
        };

        var note = CollectionMarkers.Describe(marker0);
        if (marker0 == CollectionMarker.NotCollected && stale)
            note += " (dresser snapshot is old)";

        // Appended, never replaced: whatever is already there - including a line another plugin
        // added in an earlier hook - is re-emitted first and ours lands after it.
        var built = new SeStringBuilder();
        foreach (var payload in existing.Payloads)
            built.Add(payload);

        built.Add(marker)
            .Add(RawPayload.LinkTerminator)
            .AddText("\n")
            .AddUiForeground(colour)
            .AddText($"Dungeon Drip - {note}")
            .AddUiForegroundOff();

        var bytes = built.Build().EncodeWithNullTerminator();
        strings->SetValue(ItemDescription, bytes, false);

        Trace(itemId, $"WROTE {bytes.Length} bytes ({note}); base was {existing.TextValue.Length} chars");
    }

    /// <summary>
    /// What the description field ought to contain before this adds anything to it.
    /// </summary>
    /// <remarks>
    /// The field cannot be trusted on its own. The game only writes it when the item actually has a
    /// description, and most gear has none, so it keeps whatever was there for the last item that
    /// did - which after one pass through here includes our own line. Reading it naively meant the
    /// first piece got a line and every piece after it was skipped for looking already done.
    ///
    /// So the sheet decides. Anything from our marker onwards is ours and is dropped, and what is
    /// left is only kept if it is actually this item's description; otherwise it belongs to some
    /// earlier item and goes. That also makes repeated calls idempotent by construction, which is
    /// what the marker was there for in the first place.
    ///
    /// A line another plugin added survives this, because it lands after the real description and
    /// before our marker.
    /// </remarks>
    private SeString Rebase(SeString present, uint itemId)
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

        var expected = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>()
            .TryGetRow(itemId, out var row) ? row.Description.ExtractText() : string.Empty;

        // Held over from a previous item. The sheet says this piece has nothing of its own, or has
        // something the field does not contain.
        if (expected.Length == 0 || !kept.TextValue.Contains(expected, StringComparison.Ordinal))
            return expected.Length == 0 ? new SeString() : new SeString().Append(expected);

        return kept;
    }
}
