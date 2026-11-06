using System.Linq;
using System.Numerics;
using Content.Client.Inventory;
using Content.Shared._WF.Modsuits;
using Content.Shared._WF.Modsuits.Components;
using Content.Shared._Goobstation.Clothing.Components;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WF.Modsuits;

/// <summary>
/// Projects the suit theme's hardlight mesh over every wearer sprite layer except the modsuit itself.
/// The suit's clothing sprites draw on top, leaving only the parts outside its silhouette visible.
/// </summary>
public sealed partial class ModSuitHardlightSystem : EntitySystem
{
    private const string ShaderId = "ModsuitHardlight";
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(0.25);
    private static readonly string[] SuitSlots = { "head", "outerClothing", "gloves", "shoes", "back" };

    private static readonly HumanoidVisualLayers[] ExcludedLayers =
    {
        HumanoidVisualLayers.Handcuffs,
        HumanoidVisualLayers.Ensnare,
        HumanoidVisualLayers.Fire,
        HumanoidVisualLayers.StencilMask,
    };

    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private InventorySystem _inventory = default!;

    private readonly Dictionary<EntityUid, HardlightState> _states = new();
    private TimeSpan _nextRefresh;

    public override void Shutdown()
    {
        base.Shutdown();

        foreach (var suit in _states.Keys.ToArray())
            Clear(suit);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now >= _nextRefresh)
        {
            _nextRefresh = now + RefreshInterval;
            RefreshAll();
        }

        var time = (float) now.TotalSeconds;
        foreach (var state in _states.Values)
            state.Shader.SetParameter("t", time * state.ScrollRate);
    }

    private void RefreshAll()
    {
        var seen = new HashSet<EntityUid>();
        var query = EntityQueryEnumerator<ModSuitComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            seen.Add(uid);
            Refresh((uid, comp));
        }

        foreach (var suit in _states.Keys.ToArray())
        {
            if (!seen.Contains(suit))
                Clear(suit);
        }
    }

    /// <summary>Legacy ToggleableClothing suits seal their parts instead of using ModSuitState.</summary>
    private bool HasSealedPart(EntityUid wearer)
    {
        foreach (var slot in SuitSlots)
        {
            if (_inventory.TryGetSlotEntity(wearer, slot, out var item)
                && TryComp<SealableClothingComponent>(item, out var seal)
                && seal.IsSealed)
                return true;
        }

        return false;
    }

    private void Refresh(Entity<ModSuitComponent> suit)
    {
        var theme = _proto.Index(suit.Comp.Theme);
        var wearer = suit.Comp.Wearer;

        if (!theme.Hardlight
            || wearer is not { } target
            || !TryComp<SpriteComponent>(target, out _))
        {
            Clear(suit.Owner);
            return;
        }

        if (suit.Comp.State == ModSuitState.Inactive && !HasSealedPart(target))
        {
            Clear(suit.Owner);
            return;
        }

        if (_states.TryGetValue(suit.Owner, out var existing))
        {
            if (existing.Wearer == target)
            {
                ApplyLayers(existing);
                return;
            }

            Clear(suit.Owner);
        }

        var shader = _proto.Index<ShaderPrototype>(ShaderId).InstanceUnique();
        shader.SetParameter("color", ToVector(theme.HardlightColor));
        shader.SetParameter("color2", ToVector(theme.HardlightColor2));
        shader.SetParameter("alpha", theme.HardlightAlpha);
        shader.SetParameter("intensity", theme.HardlightIntensity);
        shader.SetParameter("gridScale", theme.HardlightGridScale);
        shader.SetParameter("t", (float) _timing.CurTime.TotalSeconds * theme.HardlightScrollRate);

        var state = new HardlightState(target, shader, theme.HardlightScrollRate);
        _states[suit.Owner] = state;
        ApplyLayers(state);

        Log.Info($"Applied MOD hardlight theme '{theme.ID}' to {ToPrettyString(target)}.");
    }

    private void ApplyLayers(HardlightState state)
    {
        if (!TryComp<SpriteComponent>(state.Wearer, out var sprite))
            return;

        var skip = new HashSet<int>();

        foreach (var layer in ExcludedLayers)
        {
            if (sprite.LayerMapTryGet(layer, out var index))
                skip.Add(index);
        }

        if (TryComp<InventorySlotsComponent>(state.Wearer, out var slots))
        {
            foreach (var slot in SuitSlots)
            {
                if (!_inventory.TryGetSlotEntity(state.Wearer, slot, out var item)
                    || !IsSuitItem(item.Value, slot)
                    || !slots.VisualLayerKeys.TryGetValue(slot, out var keys))
                    continue;

                foreach (var key in keys)
                {
                    if (sprite.LayerMapTryGet(key, out var index))
                        skip.Add(index);
                }
            }
        }

        var applied = 0;
        var count = sprite.AllLayers.Count();
        for (var i = 0; i < count; i++)
        {
            if (skip.Contains(i))
                continue;

            if (ApplyLayer(sprite, state, i))
                applied++;
        }

        if (applied == 0)
            Log.Warning($"MOD hardlight found no applicable sprite layers on {ToPrettyString(state.Wearer)}.");
    }

    private bool IsSuitItem(EntityUid item, string slot)
    {
        if (HasComp<ModSuitComponent>(item) || HasComp<ModPartComponent>(item))
            return true;

        // Legacy suits use SealableClothing parts, but a back item in that slot may just be wings.
        return slot != "back" && HasComp<SealableClothingComponent>(item);
    }

    private bool ApplyLayer(SpriteComponent sprite, HardlightState state, int index)
    {
        if (!sprite.TryGetLayer(index, out var layer))
            return false;

        if (!state.Layers.TryGetValue(layer, out var original))
        {
            original = new LayerState(layer, layer.Shader, layer.ShaderPrototype?.Id, layer.Color);
            state.Layers[layer] = original;
        }
        else if (layer.Color != Color.White && layer.Color != original.Color)
        {
            original = original with { Color = layer.Color };
            state.Layers[layer] = original;
        }

        if (layer.Shader != state.Shader)
            sprite.LayerSetShader(index, state.Shader, ShaderId);

        if (layer.Color != Color.White)
            _sprite.LayerSetColor((state.Wearer, sprite), index, Color.White);

        return true;
    }

    private void Clear(EntityUid suit)
    {
        if (!_states.Remove(suit, out var state))
            return;

        if (TryComp<SpriteComponent>(state.Wearer, out var sprite))
        {
            var count = sprite.AllLayers.Count();
            for (var i = 0; i < count; i++)
            {
                if (!sprite.TryGetLayer(i, out var layer)
                    || !state.Layers.TryGetValue(layer, out var original))
                    continue;

                sprite.LayerSetShader(i, original.Shader, original.Prototype);
                _sprite.LayerSetColor((state.Wearer, sprite), i, original.Color);
            }
        }

        state.Shader.Dispose();
    }

    private static Vector3 ToVector(Color color)
    {
        return new Vector3(color.R, color.G, color.B);
    }

    private sealed class HardlightState(EntityUid wearer, ShaderInstance shader, float scrollRate)
    {
        public readonly EntityUid Wearer = wearer;
        public readonly ShaderInstance Shader = shader;
        public readonly float ScrollRate = scrollRate;
        public readonly Dictionary<SpriteComponent.Layer, LayerState> Layers = new();
    }

    private sealed record LayerState(SpriteComponent.Layer Layer, ShaderInstance? Shader, string? Prototype, Color Color);
}
