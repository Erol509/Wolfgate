using Robust.Shared.Prototypes;

namespace Content.Server._WG.BulkCargo;

/// <summary>
/// Places the two markers that open a delivery zone. One projector makes one zone and is spent; dropping it destroys it
/// and any corner it had already placed, so a single item can never open two zones.
/// </summary>
[RegisterComponent]
public sealed partial class WGCargoZoneProjectorComponent : Component
{
    /// <summary>How far from the user a marker may be placed.</summary>
    [DataField]
    public float Range = 8f;

    /// <summary>The marker entity spawned at each corner.</summary>
    [DataField]
    public EntProtoId Marker = "WGCargoZoneCorner";

    /// <summary>The first corner placed and waiting for its partner, or null before the first placement.</summary>
    [ViewVariables]
    public EntityUid? Pending;

    /// <summary>The grid the first corner stands on.</summary>
    [ViewVariables]
    public EntityUid? PendingGrid;
}
