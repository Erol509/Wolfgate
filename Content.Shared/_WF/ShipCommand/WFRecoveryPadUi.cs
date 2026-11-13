using Robust.Shared.Serialization;

namespace Content.Shared._WF.ShipCommand;

/// <summary>The recovery pad console's window.</summary>
[Serializable, NetSerializable]
public enum WFRecoveryPadUiKey : byte
{
    Key,
}

/// <summary>Appearance key for a recovery pad: true while it charges up to recall someone.</summary>
[Serializable, NetSerializable]
public enum WFRecoveryPadVisuals : byte
{
    Charging,
}

/// <summary>Sprite layers of a recovery pad: the one that shows the beam while it charges, idle otherwise.</summary>
[Serializable, NetSerializable]
public enum WFRecoveryPadLayers : byte
{
    Beam,
}

/// <summary>How a person on the pad's list is doing. Anyone neither critical nor dead needs a confirmed recall.</summary>
[Serializable, NetSerializable]
public enum WFRecoveryPadCondition : byte
{
    Alive,
    Critical,
    Dead,
}

/// <summary>Why a recall didn't start.</summary>
[Serializable, NetSerializable]
public enum WFRecoveryPadRefusal : byte
{
    /// <summary>It started: the pad is charging.</summary>
    None,

    /// <summary>The operator isn't cleared to use this pad.</summary>
    NotAllowed,

    /// <summary>There is no pad by the console.</summary>
    NoPad,

    /// <summary>The pad is already recalling someone.</summary>
    Busy,

    /// <summary>The pad is cooling down from its last recall.</summary>
    Cooldown,

    /// <summary>Not a person, or not on the pad's map (or past its range).</summary>
    OutOfReach,

    /// <summary>The person is alive and awake: the operator has to confirm first.</summary>
    NeedsConfirmation,
}

/// <summary>One person the pad can reach, with their total damage.</summary>
[Serializable, NetSerializable]
public sealed record WFRecoveryPadEntry(NetEntity Entity, string Name, WFRecoveryPadCondition Condition, string Where, float Distance, int Damage);

/// <summary>The pad console's state for one viewer: who it can reach, whether they may use it, and what it is doing.</summary>
[Serializable, NetSerializable]
public sealed class WFRecoveryPadState
{
    /// <summary>The console has a pad within reach.</summary>
    public bool HasPad;

    /// <summary>The viewer may recall someone.</summary>
    public bool Allowed;

    /// <summary>The viewer may change who may use the pad.</summary>
    public bool CanConfigure;

    /// <summary>The lowest standing with the ship that may use the pad.</summary>
    public WFShipAuthority Access = WFShipAuthority.Admin;

    /// <summary>Who is being recalled, while the pad charges.</summary>
    public string? Charging;

    /// <summary>When the charge finishes, while the pad charges.</summary>
    public TimeSpan ChargeEnds;

    /// <summary>When the pad is ready again after its last recall.</summary>
    public TimeSpan ReadyAt;

    /// <summary>Everyone the pad can reach, the dead and the worst hurt first; empty for a viewer who may not use it.</summary>
    public List<WFRecoveryPadEntry> People = new();
}

/// <summary>The console's state, sent to one viewer: whether they may use it depends on who they are.</summary>
[Serializable, NetSerializable]
public sealed class WFRecoveryPadStateMessage(WFRecoveryPadState state) : BoundUserInterfaceMessage
{
    public readonly WFRecoveryPadState State = state;
}

/// <summary>Sent by the window once it is open, and by its refresh button.</summary>
[Serializable, NetSerializable]
public sealed class WFRecoveryPadRefreshMessage : BoundUserInterfaceMessage;

/// <summary>Recall someone. <see cref="Confirmed"/> must be set for anyone alive and awake.</summary>
[Serializable, NetSerializable]
public sealed class WFRecoveryPadRecallMessage(NetEntity target, bool confirmed) : BoundUserInterfaceMessage
{
    public readonly NetEntity Target = target;
    public readonly bool Confirmed = confirmed;
}

/// <summary>Change who may use the pad: staff, officers, or admins (and the owner, always).</summary>
[Serializable, NetSerializable]
public sealed class WFRecoveryPadAccessMessage(WFShipAuthority access) : BoundUserInterfaceMessage
{
    public readonly WFShipAuthority Access = access;
}
