using Robust.Shared.Map;

namespace Content.Server._WG.BulkCargo;

/// <summary>
/// One corner of a cargo delivery zone. Two corners that share a <see cref="Zone"/> open the inclusive rectangle between
/// them, saved in grid-local tiles so a zone keeps to its ship as it moves.
/// </summary>
[RegisterComponent]
public sealed partial class WGCargoZoneCornerComponent : Component
{
    /// <summary>The zone both corners share, or empty while the corner waits for its partner.</summary>
    [ViewVariables]
    public Guid Zone;

    /// <summary>The grid the corner was placed on.</summary>
    [ViewVariables]
    public EntityUid? Grid;

    /// <summary>The tile the corner was placed on, in grid-local coordinates.</summary>
    [ViewVariables]
    public Vector2i Tile;
}
