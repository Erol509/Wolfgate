using System.Linq;
using System.Numerics;
using Content.Shared._WF.Modsuits;
using Content.Shared._WF.Modsuits.Components;
using Content.Shared.Humanoid;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
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
    [Dependency] private IResourceCache _resourceCache = default!;

    private readonly Dictionary<EntityUid, HardlightState> _states = new();
    private TimeSpan _nextRefresh;

    /// <summary>Non-body layers that should keep their normal appearance.</summary>
    private static readonly HashSet<HumanoidVisualLayers> ExcludedBaseLayers = new()
    {
        HumanoidVisualLayers.Handcuffs,
        HumanoidVisualLayers.Ensnare,
        HumanoidVisualLayers.Fire,
        HumanoidVisualLayers.StencilMask,
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

    private void Refresh(Entity<ModSuitComponent> suit)
    {
        var theme = _proto.Index(suit.Comp.Theme);
        var wearer = suit.Comp.Wearer;

        if (!theme.Hardlight
            || suit.Comp.State == ModSuitState.Inactive
            || wearer is not { } target
            || !TryComp<HumanoidAppearanceComponent>(target, out _)
            || !TryComp<SpriteComponent>(target, out _))
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
        shader.SetParameter("mask", _resourceCache.GetResource<TextureResource>(theme.HardlightMask).Texture);
        shader.SetParameter("color", ToVector(theme.HardlightColor));
        shader.SetParameter("color2", ToVector(theme.HardlightColor2));
        shader.SetParameter("alpha", theme.HardlightAlpha);
        shader.SetParameter("intensity", theme.HardlightIntensity);
        shader.SetParameter("gridScale", theme.HardlightGridScale);
        shader.SetParameter("t", (float) _timing.CurTime.TotalSeconds * theme.HardlightScrollRate);

        var state = new HardlightState(target, shader, theme.HardlightScrollRate);
        _states[suit.Owner] = state;
        ApplyLayers(state);
    }

    private void ApplyLayers(HardlightState state)
    {
        if (!TryComp<HumanoidAppearanceComponent>(state.Wearer, out var humanoid)
            || !TryComp<SpriteComponent>(state.Wearer, out var sprite))
            return;

        foreach (var layer in humanoid.BaseLayers.Keys)
        {
            if (!ExcludedBaseLayers.Contains(layer))
                ApplyLayer(sprite, state, layer);
        }

        foreach (var layer in humanoid.CustomBaseLayers.Keys)
        {
            if (!ExcludedBaseLayers.Contains(layer))
                ApplyLayer(sprite, state, layer);
        }

        // Markings (hair, ears, tails, horns, spines, …) live on their own generated layers.
        foreach (var layer in humanoid.ClientMarkingLayers)
            ApplyLayer(sprite, state, layer);
    }

    private static void ApplyLayer(SpriteComponent sprite, HardlightState state, object key)
    {
        var id = key is HumanoidVisualLayers layer ? $"enum:{layer}" : $"layer:{key}";

        if (!sprite.LayerMapTryGet(key, out var index))
            return;

        if (!state.Layers.TryGetValue(id, out var original))
        {
            sprite.TryGetLayer(index, out var spriteLayer);
            original = new LayerState(key, spriteLayer?.Shader, spriteLayer?.ShaderPrototype?.Id);
            state.Layers[id] = original;
        }

        sprite.LayerSetShader(index, state.Shader, ShaderId);
    }

    private void Clear(EntityUid suit)
    {
        if (!_states.Remove(suit, out var state))
            return;

        if (TryComp<SpriteComponent>(state.Wearer, out var sprite))
        {
            foreach (var original in state.Layers.Values)
            {
                if (!sprite.LayerMapTryGet(original.Key, out var index))
                    continue;

                sprite.LayerSetShader(index, original.Shader, original.Prototype);
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

    private sealed record LayerState(object Key, ShaderInstance? Shader, string? Prototype);
}
