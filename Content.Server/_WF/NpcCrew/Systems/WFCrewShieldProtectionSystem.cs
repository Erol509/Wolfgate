using Content.Server._WF.NpcCrew.Components;
using Content.Shared._WF.ShipShields;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>Extends autonomous crew friendly-fire protection to shield interception.</summary>
public sealed class WFCrewShieldProtectionSystem : EntitySystem
{
    [Dependency] private WFCrewFriendlyFireSystem _friendlyFire = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFShipShieldInterceptAttemptEvent>(OnIntercept);
    }

    private void OnIntercept(ref WFShipShieldInterceptAttemptEvent args)
    {
        if (args.Cancelled)
            return;
        // Launched rounds retain their controller even after the cannon changes hands.
        var source = HasComp<WFCrewShipFireComponent>(args.Shot) ? args.Shot
            : args.Weapon is { } weapon && HasComp<WFCrewShipFireComponent>(weapon) ? weapon
            : args.Shooter ?? args.Weapon;
        if (source is { } attacker && _friendlyFire.Protected(attacker, args.Grid))
            args.Cancelled = true;
    }
}
