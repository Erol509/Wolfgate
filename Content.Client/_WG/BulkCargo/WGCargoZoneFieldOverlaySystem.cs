using Robust.Client.Graphics;
using Robust.Shared.Timing;

namespace Content.Client._WG.BulkCargo;

/// <summary>Registers the delivery zone's world-space energy field overlay.</summary>
public sealed class WGCargoZoneFieldOverlaySystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        _overlays.AddOverlay(new WGCargoZoneFieldOverlay(EntityManager, _timing));
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlays.RemoveOverlay<WGCargoZoneFieldOverlay>();
    }
}
