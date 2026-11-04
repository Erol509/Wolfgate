using Content.Shared._WF.Modsuits.Components;
using Content.Shared._WF.Modsuits.Systems;
using Content.Shared.Actions;
using Robust.Shared.GameStates;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._WF.Modsuits.Modules;

/// <summary>One selectable device prototype for <see cref="ModDeviceSelectorComponent"/>.</summary>
[DataDefinition]
public sealed partial class ModDeviceSelectorEntry
{
    [DataField(required: true)]
    public EntProtoId Prototype;
}

/// <summary>Lets a device module cycle between several device prototypes.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ModDeviceSelectorComponent : Component
{
    [DataField(required: true)]
    public List<ModDeviceSelectorEntry> Entries = new();

    [DataField, AutoNetworkedField]
    public int Selected;

    [DataField]
    public EntProtoId SelectAction = "ActionModSelectDevice";

    [DataField, AutoNetworkedField]
    public EntityUid? SelectActionEntity;
}

public sealed partial class ModSelectDeviceActionEvent : InstantActionEvent;

public sealed partial class ModDeviceSelectorSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ModDeviceSelectorComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ModDeviceSelectorComponent, ModSuitWearerChangedEvent>(OnWearerChanged);
        SubscribeLocalEvent<ModDeviceSelectorComponent, ModModuleInstalledEvent>(OnInstalled);
        SubscribeLocalEvent<ModDeviceSelectorComponent, ModModuleRemovedEvent>(OnRemoved);
        SubscribeLocalEvent<ModDeviceSelectorComponent, ModSelectDeviceActionEvent>(OnSelectAction);
    }

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private ActionContainerSystem _actionContainer = default!;
    [Dependency] private ModDeviceModuleSystem _device = default!;
    [Dependency] private ModSuitSystem _modSuit = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    private void OnMapInit(Entity<ModDeviceSelectorComponent> ent, ref MapInitEvent args)
    {
        if (!_actionContainer.EnsureAction(ent, ref ent.Comp.SelectActionEntity, ent.Comp.SelectAction))
            return;

        _actions.SetEntityIcon(ent.Comp.SelectActionEntity.Value, ent.Owner);
        ApplySelection(ent);
        Dirty(ent);
    }
    private void OnWearerChanged(Entity<ModDeviceSelectorComponent> ent, ref ModSuitWearerChangedEvent args)
    {
        if (args.OldWearer is { } old)
            RemoveSelectAction(ent, old);

        if (args.NewWearer is { } wearer)
            GrantSelectAction(ent, wearer);
    }
    private void OnInstalled(Entity<ModDeviceSelectorComponent> ent, ref ModModuleInstalledEvent args)
    {
        if (TryComp<ModSuitComponent>(args.Suit, out var suit) && suit.Wearer is { } wearer)
            GrantSelectAction(ent, wearer);
    }
    private void OnRemoved(Entity<ModDeviceSelectorComponent> ent, ref ModModuleRemovedEvent args)
    {
        if (_actions.GetAction(ent.Comp.SelectActionEntity, false) is { } action && action.Comp.AttachedEntity is { } user)
            RemoveSelectAction(ent, user);
    }

    private void GrantSelectAction(Entity<ModDeviceSelectorComponent> ent, EntityUid wearer)
    {
        if (_timing.ApplyingState
            || _actions.GetAction(ent.Comp.SelectActionEntity, false) is not { } action
            || action.Comp.AttachedEntity == wearer)
            return;

        _actions.AddAction(wearer, action.AsNullable(), ent.Owner);
    }

    private void RemoveSelectAction(Entity<ModDeviceSelectorComponent> ent, EntityUid user)
    {
        if (_timing.ApplyingState
            || _actions.GetAction(ent.Comp.SelectActionEntity, false) is not { } action
            || action.Comp.AttachedEntity != user)
            return;

        _actions.RemoveProvidedAction(user, ent, action);
    }
    private void OnSelectAction(Entity<ModDeviceSelectorComponent> ent, ref ModSelectDeviceActionEvent args)
    {
        if (args.Handled
            || ent.Comp.Entries.Count == 0
            || !_modSuit.TryGetModuleSuit(ent, out var suit)
            || suit.Comp.Wearer != args.Performer
            || !_modSuit.CanAccess(suit, args.Performer))
            return;

        args.Handled = true;
        if (_net.IsClient)
            return;

        ent.Comp.Selected = (ent.Comp.Selected + 1) % ent.Comp.Entries.Count;
        ApplySelection(ent);
        Dirty(ent);
    }

    private void ApplySelection(Entity<ModDeviceSelectorComponent> ent)
    {
        if (ent.Comp.Entries.Count == 0 || !TryComp<ModDeviceModuleComponent>(ent, out var device))
            return;

        var index = Math.Clamp(ent.Comp.Selected, 0, ent.Comp.Entries.Count - 1);
        _device.SetDevice((ent.Owner, device), ent.Comp.Entries[index].Prototype);
    }
}
