using Content.Server._Crescent.ShipShields;
using Content.Shared._WF.ShipShields;

namespace Content.Server._WF.ShipShields;

/// <summary>Applies beam impacts after shared shield interception.</summary>
public sealed class WFShipShieldHitscanDamageSystem : EntitySystem
{
    [Dependency] private ShipShieldsSystem _shields = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFShipShieldVisualsComponent, WFShipShieldHitscanImpactEvent>(OnImpact);
    }

    private void OnImpact(Entity<WFShipShieldVisualsComponent> ent, ref WFShipShieldHitscanImpactEvent args)
    {
        _shields.ApplyWolfgateHitscanImpact(ent.Owner, args.Hitscan, args.Position, args.Strength, args.Gun, args.Shooter);
    }
}
