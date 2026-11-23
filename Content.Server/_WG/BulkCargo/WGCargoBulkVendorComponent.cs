using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.BulkCargo;

/// <summary>
/// Tuning for a trade vendor's bulk deliveries. Absent, the system's own defaults apply; a prototype adds it to a vendor
/// to slow or quicken the queue and change the arrival effect.
/// </summary>
[RegisterComponent]
public sealed partial class WGCargoBulkVendorComponent : Component
{
    /// <summary>Time between one crate and the next from the same vendor.</summary>
    [DataField]
    public TimeSpan DeliveryInterval = TimeSpan.FromSeconds(1.75);

    /// <summary>How long a crate may wait for a blocked tile before the rest of its order is refunded.</summary>
    [DataField]
    public TimeSpan BlockedTimeout = TimeSpan.FromMinutes(2);

    /// <summary>Effect spawned where a crate arrives.</summary>
    [DataField]
    public EntProtoId TeleportEffect = "EffectFlashBluespace";

    /// <summary>Sound played where a crate arrives.</summary>
    [DataField]
    public SoundSpecifier TeleportSound = new SoundPathSpecifier("/Audio/Machines/phasein.ogg");
}
