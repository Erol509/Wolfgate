using System.Linq;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Body.Events;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;

namespace Content.Shared._WF.Wolfmed.Surgery;

/// <summary>
/// Clears the amputation-consequence wound on a stump when a limb is surgically reattached, so a missing
/// limb can be replaced directly instead of requiring a separate stump-repair procedure first.
/// </summary>
public sealed partial class WolfmedAttachmentSystem : EntitySystem
{
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private WoundSystem _wounds = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BodyComponent, BodyPartAttachedEvent>(OnAttached);
    }

    private void OnAttached(Entity<BodyComponent> ent, ref BodyPartAttachedEvent args)
    {
        if (_body.GetParentPartOrNull(args.Part.Owner) is not { } parent ||
            !TryComp<WoundHostComponent>(ent, out var host))
            return;

        foreach (var wound in _wounds.GetWounds(parent).ToArray())
        {
            if (wound.Comp.Prototype == host.AmputationConsequenceWound &&
                _wounds.RemoveWound(wound.Owner))
            {
                // One stump wound belongs to the limb that was just attached; leave the others for the
                // remaining missing limbs.
                break;
            }
        }
    }
}
