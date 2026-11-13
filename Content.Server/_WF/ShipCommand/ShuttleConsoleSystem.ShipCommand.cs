using Content.Server._WF.ShipCommand;
using Content.Shared._WF.ShipCommand;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleConsoleSystem
{
    [Dependency] private WFShipCommandSystem _wfShipCommand = default!;

    /// <summary>A helm kept for officers opens only for officers and up of the commanded ship it stands on.</summary>
    private bool WfOfficersAllowPilot(EntityUid user, EntityUid console)
    {
        return !HasComp<WFOfficerHelmComponent>(console)
               || Transform(console).GridUid is not { } grid || !HasComp<WFShipCommandComponent>(grid)
               || _wfShipCommand.CanOperate(user, grid);
    }
}
