// WOLFGATE(Surgery): the step window is now the radial
// (the type name stays because every surgery-target prototype already registers it under SurgeryUIKey).

using Content.Client._WF.Surgery;

namespace Content.Client._Shitmed.Medical.Surgery;

// WOLFGATE(Surgery): the registered type is the radial wheel's client half.
/// <summary>The registered surgery interface: the state-driven radial wheel.</summary>
public sealed class SurgeryBui : SurgicalRadialBui
{
    public SurgeryBui(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }
}
