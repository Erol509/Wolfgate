using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._WF.Surgery;

/// <summary>Which damage a tend-wounds option treats.</summary>
public enum SurgicalHealMode : byte
{
    Brute,
    Burn,
    Combo,
}

/// <summary>A concrete operation choice presented to the surgeon.</summary>
public sealed class SurgicalOperationOption
{
    public required SurgicalOperationPrototype Operation;
    public required EntityUid Part;

    /// <summary>The organ selected for insertion or removal, when relevant.</summary>
    public EntityUid? Organ;

    /// <summary>The tool used to start the operation, for the legacy effect handlers.</summary>
    public EntityUid? Tool;

    /// <summary>Whether the organ option inserts rather than removes.</summary>
    public bool Inserting;

    public SurgicalHealMode HealMode;

    /// <summary>Localised option name.</summary>
    public string Name = string.Empty;

    /// <summary>Localised option description.</summary>
    public string Description = string.Empty;

    /// <summary>Localised name of the tool the operation wants.</summary>
    public string ToolName = string.Empty;

    /// <summary>Radial icon.</summary>
    public SpriteSpecifier? Icon;

    public bool Priority;

    /// <summary>Identifies this option within the current availability list.</summary>
    public string Key => $"{Operation.ID}|{(Organ is { } organ ? organ.Id.ToString() : "-")}|{(Inserting ? "in" : "out")}|{(byte) HealMode}";
}

/// <summary>Bound user interface key for the surgical radial.</summary>
[Serializable, NetSerializable]
public enum SurgicalRadialUiKey
{
    Key,
}

/// <summary>One radial slice, as replicated to the client.</summary>
[Serializable, NetSerializable]
public sealed class SurgicalRadialOption
{
    public int Index;
    public string Name = string.Empty;
    public string Description = string.Empty;
    public string Tool = string.Empty;
    public SpriteSpecifier? Icon;
    public bool Priority;

    public SurgicalRadialOption()
    {
    }

    public SurgicalRadialOption(int index, SurgicalOperationOption option)
    {
        Index = index;
        Name = option.Name;
        Description = option.Description;
        Tool = option.ToolName;
        Icon = option.Icon;
        Priority = option.Priority;
    }
}

/// <summary>Radial state sent to the surgeon.</summary>
[Serializable, NetSerializable]
public sealed class SurgicalRadialState : BoundUserInterfaceState
{
    public NetEntity Target;
    public NetEntity? Part;
    public List<SurgicalRadialOption> Options = new();

    public SurgicalRadialState()
    {
    }

    public SurgicalRadialState(NetEntity target, NetEntity? part, List<SurgicalRadialOption> options)
    {
        Target = target;
        Part = part;
        Options = options;
    }
}

/// <summary>Surgeon picked a radial slice.</summary>
[Serializable, NetSerializable]
public sealed class SurgicalRadialChooseMessage : BoundUserInterfaceMessage
{
    public int Index;

    public SurgicalRadialChooseMessage(int index)
    {
        Index = index;
    }
}

/// <summary>
/// Raised when a technology unlocks a surgical operation. Put on a technology's generic unlocks to make the
/// operation available station-wide.
/// </summary>
public sealed class SurgicalUnlockEvent : EntityEventArgs
{
    public EntProtoId Operation;

    public SurgicalUnlockEvent(EntProtoId operation)
    {
        Operation = operation;
    }
}
