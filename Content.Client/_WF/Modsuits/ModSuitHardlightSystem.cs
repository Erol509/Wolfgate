using System.Linq;
using System.Numerics;
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
/// Projects the suit theme's hardlight mesh over the wearer's humanoid layers while the suit is active.
/// The modsuit's clothing sprites draw over the body, so only the parts outside its silhouette remain visible.
/// </summary>
public sealed partial class ModSuitHardlightSystem : EntitySystem
{
    private const string ShaderId = "ModsuitHardlight";
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(0.25);

    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private InventorySystem _inventory = default!;

    private readonly Dictionary<EntityUid, HardlightState> _states = new();
    private TimeSpan _nextRefresh;

    /// <summary>Layers that stick out of the suit; the rest of the body stays under the suit untouched.</summary>
    private static readonly HashSet<HumanoidVisualLayers> HardlightLayers = new()
    {
        HumanoidVisualLayers.Special,
        HumanoidVisualLayers.TailExtras,
        HumanoidVisualLayers.Tail,
        HumanoidVisualLayers.TailBehind,
        HumanoidVisualLayers.TailOversuit,
        HumanoidVisualLayers.Wings,
        HumanoidVisualLayers.Hair,
        HumanoidVisualLayers.FacialHair,
        HumanoidVisualLayers.Chest,
        HumanoidVisualLayers.Head,
        HumanoidVisualLayers.Snout,
        HumanoidVisualLayers.HeadSide,
        HumanoidVisualLayers.HeadTop,
        HumanoidVisualLayers.LArmExtension,
        HumanoidVisualLayers.RArmExtension,
    };

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

        // A suit can be deleted without a state change we can subscribe to.
        foreach (var suit in _states.Keys.ToArray())
        {
            if (!seen.Contains(suit))
                Clear(suit);
        }
    }

    /// <summary>Legacy ToggleableClothing suits seal their parts instead of using ModSuitState.</summary>
    private bool HasSealedPart(EntityUid wearer)
    {
        if (!HasComp<InventoryComponent>(wearer))
            return false;

        foreach (var slot in new[] { "head", "outerClothing", "gloves", "shoes" })
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
            || !TryComp<HumanoidAppearanceComponent>(target, out _)
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
                // Markings can be added or rebuilt while the suit stays sealed.
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

        Log.Info($"Applied MOD hardlight theme '{theme.ID}' to {ToPrettyString(target)} using {theme.HardlightMask}.");
    }

    private void ApplyLayers(HardlightState state)
    {
        if (!TryComp<HumanoidAppearanceComponent>(state.Wearer, out var humanoid)
            || !TryComp<SpriteComponent>(state.Wearer, out var sprite))
            return;

        var applied = 0;

        foreach (var layer in humanoid.BaseLayers.Keys)
        {
            if (HardlightLayers.Contains(layer) && ApplyLayer(sprite, state, layer))
                applied++;
        }

        foreach (var layer in humanoid.CustomBaseLayers.Keys)
        {
            if (HardlightLayers.Contains(layer) && ApplyLayer(sprite, state, layer))
                applied++;
        }

        // Markings (hair, ears, tails, wings, horns, spines, …) live on their own generated layers.
        foreach (var layer in humanoid.ClientMarkingLayers)
        {
            if (ApplyLayer(sprite, state, layer))
                applied++;
        }

        if (applied == 0)
            Log.Warning($"MOD hardlight found no applicable sprite layers on {ToPrettyString(state.Wearer)}.");
    }

    private bool ApplyLayer(SpriteComponent sprite, HardlightState state, object key)
    {
        var id = key is HumanoidVisualLayers layer ? $"enum:{layer}" : $"layer:{key}";

        if (!sprite.LayerMapTryGet(key, out var index)
            || !sprite.TryGetLayer(index, out var spriteLayer))
            return false;

        if (!state.Layers.TryGetValue(id, out var original))
        {
            original = new LayerState(key, spriteLayer.Shader, spriteLayer.ShaderPrototype?.Id, spriteLayer.Color);
            state.Layers[id] = original;
        }
        else if (spriteLayer.Color != Color.White && spriteLayer.Color != original.Color)
        {
            // The appearance system recoloured a marking or body layer while we were active.
            original = original with { Color = spriteLayer.Color };
            state.Layers[id] = original;
        }

        if (spriteLayer.Shader != state.Shader)
            sprite.LayerSetShader(index, state.Shader, ShaderId);

        if (spriteLayer.Color != Color.White)
            _sprite.LayerSetColor((state.Wearer, sprite), index, Color.White);

        return true;
    }

    private void Clear(EntityUid suit)
    {
        if (!_states.Remove(suit, out var state))
            return;

        if (TryComp<SpriteComponent>(state.Wearer, out var sprite))
        {
            foreach (var original in state.Layers.Values)
            {
                if (!sprite.LayerMapTryGet(original.Key, out var index)
                    || !sprite.TryGetLayer(index, out var spriteLayer))
                    continue;

                if (spriteLayer.Shader != original.Shader
                    || spriteLayer.ShaderPrototype?.Id != original.Prototype)
                    sprite.LayerSetShader(index, original.Shader, original.Prototype);

                if (spriteLayer.Color != original.Color)
                    _sprite.LayerSetColor((state.Wearer, sprite), index, original.Color);
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
        public readonly Dictionary<string, LayerState> Layers = new();
    }

    private sealed record LayerState(object Key, ShaderInstance? Shader, string? Prototype, Color Color);
}
