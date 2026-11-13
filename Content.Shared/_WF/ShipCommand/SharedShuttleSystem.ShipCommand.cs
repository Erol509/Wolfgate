using Content.Shared._WF.ShipCommand;

namespace Content.Shared.Shuttles.Systems;

/// <summary>FTL limits an unrestricted grid (<see cref="WFFtlUnrestrictedComponent"/>) skips.</summary>
public abstract partial class SharedShuttleSystem
{
    /// <summary>Whether the grid may jump with other ships' helms close by.</summary>
    public bool WfIgnoresFtlProximity(EntityUid grid)
    {
        return TryComp<WFFtlUnrestrictedComponent>(grid, out var comp) && comp.IgnoreProximity;
    }

    /// <summary>Whether the grid may jump past the FTL mass limit.</summary>
    public bool WfIgnoresFtlMassLimit(EntityUid grid)
    {
        return TryComp<WFFtlUnrestrictedComponent>(grid, out var comp) && comp.IgnoreMassLimit;
    }

    /// <summary>Whether the grid may jump into exclusion zones.</summary>
    public bool WfIgnoresFtlExclusionZones(EntityUid grid)
    {
        return TryComp<WFFtlUnrestrictedComponent>(grid, out var comp) && comp.IgnoreExclusionZones;
    }
}
