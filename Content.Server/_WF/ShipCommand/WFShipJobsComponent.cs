using Content.Shared._WF.NpcCrew;
using Content.Shared._WF.ShipCommand;
using Content.Shared.Roles;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.ShipCommand;

/// <summary>
/// A commanded ship's player jobs. Each starts with no open slots, so the ship stays out of the join list until the
/// owner opens one. A player who takes a job relieves the NPC at the matching post, if there is one, and is put on
/// the command list the job names; leaving hands the post and the slot back.
/// </summary>
[RegisterComponent]
public sealed partial class WFShipJobsComponent : Component
{
    [DataField]
    public Dictionary<ProtoId<JobPrototype>, WFShipJob> Jobs = new();

    /// <summary>Jobs the owner has closed: a holder who leaves one doesn't reopen it.</summary>
    [ViewVariables]
    public HashSet<ProtoId<JobPrototype>> Closed = new();
}

/// <summary>What one of a commanded ship's jobs takes over and where its holder stands.</summary>
[DataDefinition]
public sealed partial class WFShipJob
{
    /// <summary>The crew role whose post the job takes over.</summary>
    [DataField]
    public ProtoId<WFCrewRolePrototype>? Role;

    /// <summary>The trader whose counter the job takes over.</summary>
    [DataField]
    public EntProtoId? Counter;

    /// <summary>The command list the holder is put on while they hold the job: officers or staff.</summary>
    [DataField]
    public WFShipCommandList? Standing;
}

/// <summary>A player holding one of a commanded ship's jobs.</summary>
[RegisterComponent]
public sealed partial class WFShipJobHolderComponent : Component
{
    [ViewVariables]
    public EntityUid Grid;

    [ViewVariables]
    public EntityUid Station;

    [ViewVariables]
    public ProtoId<JobPrototype> Job;

    [ViewVariables]
    public NetUserId User;

    /// <summary>The roster post they took over, if any.</summary>
    [ViewVariables]
    public string? Slot;

    /// <summary>The command list the job put them on, to take them off again when they leave; null when they were already there.</summary>
    [ViewVariables]
    public WFShipCommandList? Standing;

    /// <summary>Their entry on a lower command list before the job raised them, put back when they leave.</summary>
    [ViewVariables]
    public (WFShipCommandList List, WFShipCommandPerson Person)? Restore;
}
