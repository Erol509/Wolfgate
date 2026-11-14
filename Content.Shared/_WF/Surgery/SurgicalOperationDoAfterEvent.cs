using Content.Shared.DoAfter;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Surgery;

/// <summary>
/// Completes one state-driven surgical operation. Raised on the surgeon after the do-after.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class SurgicalOperationDoAfterEvent : DoAfterEvent
{
    [DataField]
    public ProtoId<SurgicalOperationPrototype> Operation = default!;

    [DataField]
    public NetEntity TargetEntity;

    [DataField]
    public NetEntity? Part;

    [DataField]
    public NetEntity? UsedTool;

    [DataField]
    public NetEntity? Organ;

    [DataField]
    public bool Inserting;

    [DataField]
    public SurgicalHealMode HealMode;

    public override DoAfterEvent Clone() => new SurgicalOperationDoAfterEvent
    {
        Operation = Operation,
        TargetEntity = TargetEntity,
        Part = Part,
        UsedTool = UsedTool,
        Organ = Organ,
        Inserting = Inserting,
        HealMode = HealMode,
    };
}
