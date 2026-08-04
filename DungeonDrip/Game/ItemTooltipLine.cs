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
/// Adds one line to the game's own item tooltip saying whether the piece is in the collection.
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
    /// Four icons for eight states, because the word beside it already says which of the eight it is.
    /// The stars are a scale of how well collected a piece is - gold for put away, silver for a set
    /// that still has gaps - and the no-entry sign is the absence of it.
    ///
    /// Carried gets a diamond rather than a third grade of star, because it is not a worse kind of
    /// stored, it is a different thing entirely: the piece is on the character and in no box at all,
    /// one trip to a vendor from being gone. A different shape says that where a dimmer star would
    /// imply it is nearly there.
    ///
    /// The green dot the first attempt used for the dresser is gone and should stay gone. It is the
    /// game's "this is new" badge, and beside the stars it read as pasted in rather than as part of
    /// the same vocabulary.
    ///
    /// This lives here rather than beside the marker vocabulary in Core, which is deliberately free
    /// of anything Dalamud.
    /// </remarks>
    private static BitmapFontIcon Glyph(CollectionMarker marker) => marker switch
    {
        CollectionMarker.Dresser => BitmapFontIcon.GoldStar,
        CollectionMarker.Armoire => BitmapFontIcon.GoldStar,
        CollectionMarker.OutfitComplete => BitmapFontIcon.GoldStar,

        // Owned, but the set it is in still has gaps.
        CollectionMarker.Outfit => BitmapFontIcon.SilverStar,

        // In a stored outfit, but the strict rule wants the others too. Third grade of star, because
        // this is the one state where the star scale is doing real work: it is further along than the
        // no-entry sign and not as far as the silver, and the count beside it says by how much.
        CollectionMarker.OutfitPartial => BitmapFontIcon.BlueStar,

        // On the character, in a retainer's bags, or on a retainer's back: owned, and in no box at all.
        // One shape for all three, because the word beside it is what distinguishes them and more
        // diamonds would not add anything.
        CollectionMarker.Inventory => BitmapFontIcon.OrangeDiamond,
        CollectionMarker.Retainer => BitmapFontIcon.OrangeDiamond,
        CollectionMarker.RetainerEquipped => BitmapFontIcon.OrangeDiamond,

        _ => BitmapFontIcon.NoCircle,
    };

    /// <summary>
    /// Where it is, in one word. The icon has already said whether it is collected.
    /// </summary>
    /// <remarks>
    /// Short because this shares a row with the item's category and that row is not wide. The full
    /// sentences the panels use do not fit and do not need to: there are eight states and the icon
    /// carries most of the meaning.
    ///
    /// The outfit count is the one thing here that is not a fixed string, and it is the exception
    /// worth making: without it the strict ownership rule calls a piece sitting in the dresser not
    /// owned, which is indistinguishable from the plugin being broken. Two small numbers are
    /// still bounded - a piece belongs to a handful of sets at most - so the row cannot outgrow its
    /// space.
    /// </remarks>
    private static string Word(CollectionMarker marker, bool stale, int stored, int total) => marker switch
    {
        CollectionMarker.Dresser => "Dresser",
        CollectionMarker.Armoire => "Armoire",
        CollectionMarker.Outfit => "Outfit",
        CollectionMarker.OutfitComplete => "Outfit",
        CollectionMarker.Inventory => "Carried",
        CollectionMarker.Retainer => "Retainer",

        // Spelled out rather than shortened to "Retainer" like the bags are. This is the state that
        // sends people searching the wrong place, and the row has space for two more words.
        CollectionMarker.RetainerEquipped => "Retainer, worn",

        // Not a word, on purpose. "Not owned" here would be the bug report; the fraction is the
        // whole message, and the panels have room to explain it.
        CollectionMarker.OutfitPartial => $"Outfit {stored}/{total}",

        // The one worth acting on, and the one an old snapshot can be wrong about, so it is the
        // only one that spends characters on a caveat.
        _ => stale ? "Not owned?" : "Not owned",
    };

    /// <summary>This plugin's own, so the line can recognise itself and never double up.</summary>
    private const uint MarkerCommandId = 0x44445F31;

    private delegate void* GenerateItemTooltipDelegate(
        AtkUnitBase* addon, NumberArrayData* numbers, StringArrayData* strings);

    private readonly Plugin plugin;
    private readonly Hook<GenerateItemTooltipDelegate>? hook;
    private readonly DalamudLinkPayload marker;

    private bool warned;

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
            return;

        var hovered = Plugin.GameGui.HoveredItem;
        if (hovered is <= 0 or > uint.MaxValue)
            return;

        var (itemId, kind) = ItemUtil.GetBaseId((uint)hovered);

        // Key items sit behind an offset sharing the id space with real gear, and a collectable is
        // a turn-in rather than a glamour decision. HQ is not excluded - HQ gear is real gear, and
        // the base id is what the dresser stores.
        if (itemId == 0 || kind is ItemKind.EventItem or ItemKind.Collectible)
            return;

        // The same content rule as every other surface, so the absence of a line keeps meaning
        // "not glamour gear" rather than "it is collected".
        if (!plugin.Storage.CanBeStored(itemId))
            return;

        var view = plugin.Ownership.Current;
        var sets = plugin.Outfits.SetsContaining(itemId);
        var source = MissingItems.Resolve(
            itemId,
            view,
            sets,
            plugin.Configuration.OutfitOwnership,
            plugin.Configuration.Scope);

        // Meaningless for anything not held in a set, and walking a set's pieces is not free.
        // Missing this is what made a finished outfit and a half-finished one look identical.
        var completed = source == OwnershipSource.Outfit && plugin.Outfits.IsInCompletedSet(itemId, view);

        // Missing this is what made a piece the strict rule holds back look like one never collected.
        var (stored, total) = MissingItems.Shortfall(
            itemId, view, sets, source, plugin.Configuration.OutfitOwnership, plugin.Configuration.Scope);

        var state = CollectionMarkers.For(source, plugin.Ownership.HasDresserData, completed, stored > 0);

        // A tooltip has no room to explain why it cannot say, so it says nothing.
        if (state == CollectionMarker.Unknown)
            return;

        var existing = Rebase(Read(strings, ItemUiCategory));

        var stale = plugin.Ownership.IsDresserStale;
        var colour = state switch
        {
            CollectionMarker.NotCollected => stale ? (ushort)26 : (ushort)14,

            // Not the red the missing pieces get. One of these is stored; what is not is every copy
            // the chosen setting asks for.
            CollectionMarker.OutfitPartial => (ushort)26,
            CollectionMarker.Inventory => (ushort)26,

            // The same as carried, for the same reason: one is owned, it is just not put away.
            CollectionMarker.Retainer => (ushort)26,
            CollectionMarker.RetainerEquipped => (ushort)26,
            _ => (ushort)45,
        };

        // Appended, never replaced: whatever is already there - including a line another plugin
        // added in an earlier hook - is re-emitted first and this one lands after it.
        var built = new SeStringBuilder();
        foreach (var payload in existing.Payloads)
            built.Add(payload);

        // Inline, and it has to stay inline: the tooltip's rows sit at fixed positions, so a row
        // given a second line draws over the row beneath it rather than pushing it down.
        built.Add(marker)
            .Add(RawPayload.LinkTerminator)
            .AddText("   ")
            .Add(new IconPayload(Glyph(state)))
            .AddUiForeground(colour)
            .AddText(Word(state, stale, stored, total))
            .AddUiForegroundOff();

        strings->SetValue(ItemUiCategory, built.Build().EncodeWithNullTerminator(), false);
    }

    /// <summary>
    /// The line as the game wrote it, with anything this hook added last time removed.
    /// </summary>
    /// <remarks>
    /// Truncating at this plugin's own marker rather than checking for its presence and giving up. That check
    /// was the first attempt and it deadlocked the feature: it was reading a field that goes stale,
    /// so after one write every later item looked already done. Rebuilding from the game's own text
    /// each time is idempotent whether the field is fresh or not, which is the property actually
    /// wanted. A line another plugin added survives, since it lands before this one's marker.
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
