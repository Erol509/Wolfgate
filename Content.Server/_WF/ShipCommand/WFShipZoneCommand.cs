using System.Linq;
using Content.Server.Administration;
using Content.Shared._WF.ShipCommand;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._WF.ShipCommand;

/// <summary>
/// A commanded ship's zones from the admin console (the ship command console's Zones tab does the same): list them,
/// or switch one or all of them. The grid is an entity id, or "here" for the grid the admin stands on.
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed partial class WFShipZoneCommand : LocalizedEntityCommands
{
    [Dependency] private WFShipCommandSystem _command = default!;

    public override string Command => "wf_shipzone";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length is not (2 or 4) || args.Length == 2 && args[0] != "list" || args.Length == 4 && args[0] != "set")
        {
            shell.WriteLine(Help);
            return;
        }

        if (!WFShipCommandConsole.TryGetGrid<WFShipZonesComponent>(shell, EntityManager, args[1], out var grid, out var zones)
            || !WFShipCommandConsole.CanConfigure(shell, _command, grid))
            return;

        if (args[0] == "list")
        {
            foreach (var zone in zones.Zones)
            {
                shell.WriteLine(Loc.GetString("cmd-wf_shipzone-line",
                    ("id", zone.Id),
                    ("name", Loc.GetString(zone.Name)),
                    ("mode", zone.Mode.ToString()),
                    ("doors", _command.ZoneDoors(grid, zone.Id).Count)));
            }

            if (zones.Zones.Count == 0)
                shell.WriteLine(Loc.GetString("cmd-wf_shipzone-none"));
            return;
        }

        if (!Enum.TryParse<WFShipZoneMode>(args[3], true, out var mode) || !Enum.IsDefined(mode))
        {
            shell.WriteError(Loc.GetString("cmd-wf_shipzone-bad-mode"));
            return;
        }

        var ids = args[2] == "all" ? zones.Zones.Select(z => z.Id).ToList() : new List<string> { args[2] };
        if (args[2] != "all" && !_command.TryGetZone(zones, args[2], out _))
        {
            shell.WriteError(Loc.GetString("cmd-wf_shipzone-bad-zone", ("zone", args[2])));
            return;
        }

        var changed = 0;
        foreach (var id in ids)
        {
            if (_command.SetZoneMode((grid, zones), id, mode, shell.Player?.AttachedEntity))
                changed++;
        }

        shell.WriteLine(Loc.GetString("cmd-wf_shipzone-set", ("count", changed), ("mode", mode.ToString())));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(new[] { "list", "set" }, Loc.GetString("cmd-wf_shipzone-hint-sub")),
            2 => CompletionResult.FromHintOptions(new[] { "here" }, Loc.GetString("cmd-wf_ship-hint-grid")),
            3 when args[0] == "set" => CompletionResult.FromHint(Loc.GetString("cmd-wf_shipzone-hint-zone")),
            4 when args[0] == "set" => CompletionResult.FromHintOptions(
                Enum.GetNames<WFShipZoneMode>().Select(n => n.ToLowerInvariant()),
                Loc.GetString("cmd-wf_shipzone-hint-mode")),
            _ => CompletionResult.Empty,
        };
    }
}
