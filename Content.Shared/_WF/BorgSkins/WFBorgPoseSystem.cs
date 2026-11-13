using Content.Shared.Actions;
using Content.Shared.Movement.Events;
using Content.Shared.Silicons.Borgs;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.BorgSkins;

/// <summary>Gives a borg the pose actions its skin supports and tracks the pose it holds. Poses are cosmetic.</summary>
public sealed partial class WFBorgPoseSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private SharedActionsSystem _actions = default!;

    private static readonly Dictionary<WFBorgPose, EntProtoId> PoseActions = new()
    {
        { WFBorgPose.Rest, "WFActionBorgPoseRest" },
        { WFBorgPose.Sit, "WFActionBorgPoseSit" },
        { WFBorgPose.BellyUp, "WFActionBorgPoseBellyUp" },
    };

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BorgSwitchableSubtypeComponent, WFBorgSubtypeSelectedEvent>(OnSubtypeSelected);
        SubscribeLocalEvent<WFBorgPoseComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<WFBorgPoseComponent, WFBorgPoseActionEvent>(OnPoseAction);
        SubscribeLocalEvent<WFBorgPoseComponent, MoveInputEvent>(OnMoveInput);
    }

    private void OnSubtypeSelected(Entity<BorgSwitchableSubtypeComponent> ent, ref WFBorgSubtypeSelectedEvent args)
    {
        if (_net.IsClient)
            return;

        RemComp<WFBorgPoseComponent>(ent);

        if (!_prototype.TryIndex<WFBorgSkinPrototype>(args.Subtype.Id, out var skin) || skin.Poses.Count == 0)
            return;

        var pose = AddComp<WFBorgPoseComponent>(ent);
        foreach (var offered in skin.Poses)
        {
            if (PoseActions.TryGetValue(offered, out var action) && _actions.AddAction(ent, action) is { } added)
                pose.Actions.Add(added);
        }
    }

    private void OnShutdown(Entity<WFBorgPoseComponent> ent, ref ComponentShutdown args)
    {
        foreach (var action in ent.Comp.Actions)
        {
            _actions.RemoveAction(ent.Owner, action);
            if (_net.IsServer)
                QueueDel(action);
        }
    }

    private void OnPoseAction(Entity<WFBorgPoseComponent> ent, ref WFBorgPoseActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        SetPose(ent, ent.Comp.Pose == args.Pose ? WFBorgPose.None : args.Pose);
    }

    private void OnMoveInput(Entity<WFBorgPoseComponent> ent, ref MoveInputEvent args)
    {
        if (args.HasDirectionalMovement)
            SetPose(ent, WFBorgPose.None);
    }

    private void SetPose(Entity<WFBorgPoseComponent> ent, WFBorgPose pose)
    {
        if (ent.Comp.Pose == pose)
            return;

        ent.Comp.Pose = pose;
        Dirty(ent);
    }
}
