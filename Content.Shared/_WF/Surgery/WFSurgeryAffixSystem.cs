using Content.Shared._Shitmed.Body.Events;
using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._Shitmed.Medical.Surgery.Steps.Parts;
using Content.Shared.Body.Systems;

namespace Content.Shared._WF.Surgery;

/// <summary>
/// Affixes parts and organs reattached during an operation, so the generic closing step finishes the
/// procedure without a second, per-surgery "seal wounds" operation.
/// </summary>
public sealed partial class WFSurgeryAffixSystem : EntitySystem
{
    [Dependency] private SharedBodySystem _body = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFSurgeryAffixOnCloseComponent, SurgeryStepEvent>(OnStep);
    }

    private void OnStep(Entity<WFSurgeryAffixOnCloseComponent> ent, ref SurgeryStepEvent args)
    {
        foreach (var (part, _) in _body.GetBodyPartChildren(args.Part))
        {
            if (!HasComp<BodyPartReattachedComponent>(part))
                continue;

            RemComp<BodyPartReattachedComponent>(part);
            var enabled = new BodyPartEnableChangedEvent(true);
            RaiseLocalEvent(part, ref enabled);
        }

        foreach (var (organ, _) in _body.GetPartOrgans(args.Part))
        {
            if (HasComp<OrganReattachedComponent>(organ))
                RemComp<OrganReattachedComponent>(organ);
        }
    }
}
