using Content.Shared._Shitmed.Targeting;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Surgery;

/// <summary>
/// An operating computer: monitors the patient on an adjacent operating table and lists the operations the
/// patient's surgical state permits.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OperatingComputerComponent : Component
{
    /// <summary>Adjacent operating table, if any.</summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Table;

    /// <summary>Patient currently buckled to the table.</summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Patient;

    /// <summary>The body zone the computer is showing.</summary>
    [DataField, AutoNetworkedField]
    public TargetBodyPart Zone = TargetBodyPart.Torso;
}

/// <summary>Bound user interface key of the operating computer.</summary>
[Serializable, NetSerializable]
public enum OperatingComputerUiKey
{
    Key,
}

/// <summary>One operation row in the computer's catalog or next-step list.</summary>
[Serializable, NetSerializable]
public sealed class OperatingComputerOperationState
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public string Description = string.Empty;
    public string Tool = string.Empty;
    public bool ShowAsNext;
    public bool Priority;
    public bool Mechanic;
    public bool Locked;
    public bool InList = true;
    public List<string> Requirements = new();
}

/// <summary>Everything the operating computer shows.</summary>
[Serializable, NetSerializable]
public sealed class OperatingComputerState : BoundUserInterfaceState
{
    public bool HasTable;
    public bool HasPatient;
    public int Zone = (int) TargetBodyPart.Torso;

    public string PatientState = string.Empty;
    public string PatientStateColor = "good";
    public float Health;
    public float MaxHealth;
    public float MinHealth;
    public float BloodLevel;
    public float StandardBloodLevel;
    public string BloodType = string.Empty;
    public float Brute;
    public float Burn;
    public float Toxin;
    public float Oxy;
    public List<string> SurgeryState = new();
    public bool PatientMechanical;

    /// <summary>Operation ids the patient's current state permits.</summary>
    public List<string> AvailableOperations = new();

    /// <summary>Full entries for the operations the state permits, so the next-step list needs no catalog.</summary>
    public List<OperatingComputerOperationState> NextOperations = new();

    public bool HasTechweb;
    public List<string> Experiments = new();
}

/// <summary>The full operation catalog; only changes when research unlocks something.</summary>
[Serializable, NetSerializable]
public sealed class OperatingComputerCatalogState : BoundUserInterfaceState
{
    public List<OperatingComputerOperationState> Operations = new();
}

/// <summary>Asks the computer to show another body zone.</summary>
[Serializable, NetSerializable]
public sealed class OperatingComputerChangeZoneMessage : BoundUserInterfaceMessage
{
    public int Zone;

    public OperatingComputerChangeZoneMessage(int zone)
    {
        Zone = zone;
    }
}

/// <summary>Asks the computer to open its experiment configuration.</summary>
[Serializable, NetSerializable]
public sealed class OperatingComputerOpenExperimentsMessage : BoundUserInterfaceMessage;
