using Robust.Shared.GameStates;
using Robust.Shared.Map;

namespace Content.Shared._WG.BulkCargo;

/// <summary>
/// A painted delivery zone's energy field. One invisible entity carries the rectangle in grid-local tiles; the client
/// draws it as a single rippling pass, so a zone costs one networked entity rather than one per tile.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WGCargoZoneFieldComponent : Component
{
    /// <summary>Lowest tile of the rectangle, inclusive.</summary>
    [DataField, AutoNetworkedField]
    public Vector2i Min;

    /// <summary>Highest tile of the rectangle, inclusive.</summary>
    [DataField, AutoNetworkedField]
    public Vector2i Max;
}
