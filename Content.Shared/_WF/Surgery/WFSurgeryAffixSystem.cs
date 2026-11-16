using Content.Shared._Shitmed.Body.Events;
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

    /// <summary>Clears the reattached markers on the operated part after the step that inserted them.</summary>
    public void Affix(EntityUid part)
    {
        foreach (var (child, _) in _body.GetBodyPartChildren(part))
        {
            if (!HasComp<BodyPartReattachedComponent>(child))
                continue;

            RemComp<BodyPartReattachedComponent>(child);
            var enabled = new BodyPartEnableChangedEvent(true);
            RaiseLocalEvent(child, ref enabled);
        }

        foreach (var (organ, _) in _body.GetPartOrgans(part))
        {
            if (HasComp<OrganReattachedComponent>(organ))
                RemComp<OrganReattachedComponent>(organ);
        }
    }
}
