using Content.Shared._WF.ShipCommand;
using Content.Shared.Popups;
using Content.Shared.Wires;

namespace Content.Server._WF.ShipCommand;

public sealed partial class WFShipCommandSystem
{
    [Dependency] private SharedPopupSystem _popup = default!;

    private void InitializeWires()
    {
        SubscribeLocalEvent<WiresPanelComponent, AttemptChangePanelEvent>(OnAttemptChangePanel);
        SubscribeLocalEvent<WFEmagAttemptEvent>(OnEmagAttempt);
    }

    /// <summary>On a ship with staff-only wires, an emag works for the staff list and up alone.</summary>
    private void OnEmagAttempt(ref WFEmagAttemptEvent args)
    {
        if (args.Cancelled || CanTouchWires(args.User, args.Target))
            return;

        args.Cancelled = true;
        _popup.PopupEntity(Loc.GetString("wf-shipcommand-emag-staff-only"), args.Target, args.User);
    }

    /// <summary>On a ship with staff-only wires, a maintenance panel opens and closes for the staff list and up alone.</summary>
    private void OnAttemptChangePanel(Entity<WiresPanelComponent> panel, ref AttemptChangePanelEvent args)
    {
        if (args.Cancelled || args.User is not { } user || CanTouchWires(user, panel))
            return;

        args.Cancelled = true;
        _popup.PopupEntity(Loc.GetString("wf-shipcommand-wires-staff-only"), panel, user);
    }

    /// <summary>Whether someone may work an entity's maintenance panel or emag it: anyone, unless it is aboard a ship
    /// with <see cref="WFShipCommandComponent.StaffOnlyWires"/>, where the staff list and up.</summary>
    public bool CanTouchWires(EntityUid user, EntityUid target)
    {
        if (Transform(target).GridUid is not { } grid
            || !TryComp<WFShipCommandComponent>(grid, out var command)
            || !command.StaffOnlyWires)
            return true;

        return GetAuthority(user, grid) >= WFShipAuthority.Staff;
    }
}
