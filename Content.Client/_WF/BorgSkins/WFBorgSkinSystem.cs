using System.Numerics;
using Content.Shared._WF.BorgSkins;
using Content.Shared.Silicons.Borgs;
using Content.Shared.Silicons.Borgs.Components;
using Robust.Client.GameObjects;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WF.BorgSkins;

/// <summary>Places oversized borg skins on their tile and plays their idle and walking animations.</summary>
public sealed partial class WFBorgSkinSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    private const string MovingSuffix = "_moving";
    private const float MovingSpeedSquared = 0.04f;

    private static readonly Enum[] ChassisLayers =
    {
        BorgVisualLayers.Body,
        BorgVisualLayers.Light,
        BorgVisualLayers.LightStatus,
    };

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BorgSwitchableSubtypeComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<BorgSwitchableSubtypeComponent, AfterAutoHandleStateEvent>(OnHandleState);
    }

    private void OnStartup(Entity<BorgSwitchableSubtypeComponent> ent, ref ComponentStartup args)
    {
        ApplySkin(ent);
    }

    private void OnHandleState(Entity<BorgSwitchableSubtypeComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        ApplySkin(ent);
    }

    private void ApplySkin(Entity<BorgSwitchableSubtypeComponent> ent)
    {
        if (!TryComp(ent, out SpriteComponent? sprite))
            return;

        WFBorgSkinPrototype? skin = null;
        if (ent.Comp.BorgSubtype is { } subtype)
            _prototype.TryIndex<WFBorgSkinPrototype>(subtype.Id, out skin);

        var offset = skin?.Offset ?? Vector2.Zero;
        foreach (var key in ChassisLayers)
        {
            if (_sprite.TryGetLayer((ent.Owner, sprite), key, out var layer, false))
                _sprite.LayerSetOffset(layer, offset);
        }

        if (skin is { Animated: true } or { Walks: true })
            EnsureComp<WFBorgSkinAnimatedComponent>(ent).Walks = skin.Walks;
        else
            RemComp<WFBorgSkinAnimatedComponent>(ent);
    }

    public override void FrameUpdate(float frameTime)
    {
        var now = _timing.RealTime.TotalSeconds;
        var query = EntityQueryEnumerator<WFBorgSkinAnimatedComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var skin, out var sprite))
        {
            var moving = skin.Walks
                         && TryComp(uid, out PhysicsComponent? body)
                         && body.LinearVelocity.LengthSquared() > MovingSpeedSquared;

            foreach (var key in ChassisLayers)
            {
                if (!_sprite.TryGetLayer((uid, sprite), key, out var layer, false)
                    || layer.ActualRsi is not { } rsi
                    || layer.State.Name is not { } name)
                    continue;

                // Other systems set the idle state names, so the moving variant is re-applied whenever it is lost.
                if (skin.Walks && moving != name.EndsWith(MovingSuffix))
                {
                    var wanted = moving ? name + MovingSuffix : name[..^MovingSuffix.Length];
                    if (rsi.TryGetState(wanted, out _))
                        _sprite.LayerSetRsiState(layer, wanted);
                }

                // Hidden layers don't advance, so every layer is pinned to the clock to keep eyes and lamps on the body.
                if (rsi.TryGetState(layer.State, out var state) && state.IsAnimated)
                    _sprite.LayerSetAnimationTime(layer, (float)(now % state.TotalDelay));
            }
        }
    }
}
