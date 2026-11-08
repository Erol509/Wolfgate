using System.Linq;
using System.Numerics;
using Content.Shared._WF.Modsuits;
using Content.Shared._WF.Modsuits.Components;
using Content.Shared._Goobstation.Clothing.Components;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WF.Modsuits;

/// <summary>
/// Projects the suit theme's hardlight mesh over the wearer's body and marking layers while the suit is active.
/// Clothing layers are never touched, so the effect cannot show through the suit's armour.
/// </summary>
public sealed partial class ModSuitHardlightSystem : EntitySystem
{
    private const string ShaderId = "ModsuitHardlight";
    private const string MaskLayerKey = "wf-hardlight-mask";
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(0.25);
    private static readonly string[] SuitSlots = { "head", "outerClothing", "gloves", "shoes", "back" };

    private static readonly HashSet<HumanoidVisualLayers> ExcludedLayers = new()
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
    [Dependency] private IResourceCache _cache = default!;

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
            || !TryComp<SpriteComponent>(target, out var sprite))
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

        if (!_cache.TryGetResource<TextureResource>(theme.HardlightMask, out _)
            || !theme.HardlightMask.TryRelativeTo(SpriteSystem.TextureRoot, out var relativeMask))
        {
            Log.Warning($"MOD hardlight theme '{theme.ID}' could not load mask {theme.HardlightMask}.");
            Clear(suit.Owner);
            return;
        }

        var shader = _proto.Index<ShaderPrototype>(ShaderId).InstanceUnique();
        shader.SetParameter("color", ToVector(theme.HardlightColor));
        shader.SetParameter("color2", ToVector(theme.HardlightColor2));
        shader.SetParameter("alpha", theme.HardlightAlpha);
        shader.SetParameter("intensity", theme.HardlightIntensity);
        shader.SetParameter("maskScale", GetMaskScale(sprite));

        var state = new HardlightState(target, shader, relativeMask.Value);
        _states[suit.Owner] = state;
        ApplyLayers(state);

        Log.Info($"Applied MOD hardlight theme '{theme.ID}' to {ToPrettyString(target)}.");
    }

    /// <summary>How many 32-pixel mask tiles fit across the sprite atlas.</summary>
    private static float GetMaskScale(SpriteComponent sprite)
    {
        foreach (var layer in sprite.AllLayers)
        {
            if (layer.Rsi is { } rsi
                && rsi.TryGetState(layer.RsiState, out var state)
                && state.Frame0 is AtlasTexture atlas)
                return atlas.SourceTexture.Width / 32f;
        }

        return 384f;
    }

    private void ApplyLayers(HardlightState state)
    {
        if (!TryComp<SpriteComponent>(state.Wearer, out var sprite))
            return;

        var applied = 0;
        object? maskTarget = null;

        // Body layers are keyed by the enum on the sprite. The client's copy of BaseLayers is not
        // networked and stays empty, so the enum is the only reliable source for base layers.
        foreach (var layer in Enum.GetValues<HumanoidVisualLayers>())
        {
            if (ExcludedLayers.Contains(layer))
                continue;

            if (ApplyLayer(sprite, state, layer))
            {
                applied++;
                maskTarget ??= layer;
            }
        }

        // Markings (hair, ears, tails, wings, horns, spines, …) live on their own generated layers.
        if (TryComp<HumanoidAppearanceComponent>(state.Wearer, out var humanoid))
        {
            foreach (var layer in humanoid.ClientMarkingLayers)
            {
                if (ApplyLayer(sprite, state, layer))
                {
                    applied++;
                    maskTarget ??= layer;
                }
            }
        }

        if (applied == 0)
            Log.Warning($"MOD hardlight found no applicable sprite layers on {ToPrettyString(state.Wearer)}.");
        else if (!state.MaskLayerAdded && maskTarget != null)
            AddMaskLayer(state, sprite, maskTarget);
    }

    private void AddMaskLayer(HardlightState state, SpriteComponent sprite, object target)
    {
        // Enum layer keys need the enum reference syntax; marking layers are plain strings.
        var targetKey = target is Enum enumKey
            ? $"enum.{enumKey.GetType().Name}.{enumKey}"
            : target.ToString()!;

        var layer = new PrototypeLayerData
        {
            TexturePath = state.MaskPath.ToString(),
            CopyToShaderParameters = new PrototypeCopyToShaderParameters
            {
                LayerKey = targetKey,
                ParameterTexture = "maskTex",
            },
        };

        _sprite.AddLayer((state.Wearer, sprite), layer, 0);
        _sprite.LayerMapSet((state.Wearer, sprite), MaskLayerKey, 0);
        state.MaskLayerAdded = true;
    }

    private bool ApplyLayer(SpriteComponent sprite, HardlightState state, object key)
    {
        if (!sprite.LayerMapTryGet(key, out var index)
            || !sprite.TryGetLayer(index, out var spriteLayer))
            return false;

        if (!state.Layers.TryGetValue(spriteLayer, out var original))
        {
            original = new LayerState(spriteLayer, spriteLayer.Shader, spriteLayer.ShaderPrototype?.Id, spriteLayer.Color);
            state.Layers[spriteLayer] = original;
        }
        else if (spriteLayer.Color != Color.White && spriteLayer.Color != original.Color)
        {
            // The appearance system recoloured a marking or body layer while we were active.
            original = original with { Color = spriteLayer.Color };
            state.Layers[spriteLayer] = original;
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
            if (state.MaskLayerAdded)
                _sprite.RemoveLayer((state.Wearer, sprite), MaskLayerKey, false);

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

    private sealed class HardlightState(EntityUid wearer, ShaderInstance shader, ResPath maskPath)
    {
        public readonly EntityUid Wearer = wearer;
        public readonly ShaderInstance Shader = shader;
        public readonly ResPath MaskPath = maskPath;
        public readonly Dictionary<SpriteComponent.Layer, LayerState> Layers = new();
        public bool MaskLayerAdded;
    }

    private sealed record LayerState(SpriteComponent.Layer Layer, ShaderInstance? Shader, string? Prototype, Color Color);
}
