using System.Globalization;
using System.Linq;
using Content.Server.Administration;
using Content.Shared._WF.ShipCommand;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._WF.ShipCommand;

/// <summary>
/// A commanded ship's price rules from the admin console (the ship command console's Prices tab does the same): list
/// them, add or replace one, switch one (or the owner's and admins' free service) on and off, or remove one.
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed partial class WFShipPriceCommand : LocalizedEntityCommands
{
    [Dependency] private WFShipCommandSystem _command = default!;
    [Dependency] private WFShipServiceSystem _services = default!;

    public override string Command => "wf_shipprice";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 2)
        {
            shell.WriteLine(Help);
            return;
        }

        if (!WFShipCommandConsole.TryGetGrid<WFShipCommandComponent>(shell, EntityManager, args[1], out var grid, out var command)
            || !WFShipCommandConsole.CanConfigure(shell, _command, grid))
            return;

        var ship = (grid, command);
        switch (args[0])
        {
            case "list" when args.Length == 2:
                shell.WriteLine(Loc.GetString("cmd-wf_shipprice-free", ("owner", command.OwnerServedFree), ("admins", command.AdminsServedFree)));
                foreach (var rule in command.PriceRules)
                {
                    shell.WriteLine(Loc.GetString("cmd-wf_shipprice-line",
                        ("name", rule.Name),
                        ("audience", rule.Audience == WFShipPriceAudience.Company ? $"company:{rule.Company}" : rule.Audience.ToString()),
                        ("services", rule.Services.ToString()),
                        ("multiplier", rule.Multiplier.ToString("0.##", CultureInfo.InvariantCulture)),
                        ("enabled", rule.Enabled)));
                }
                break;
            case "add" when args.Length == 6:
                if (args[2].Length > WFShipConsoleSystem.MaxRuleName || args[2] is "owner" or "admins"
                    || !TryParseAudience(args[3], out var audience, out var company)
                    || company?.Length > WFShipConsoleSystem.MaxRuleName
                    || !TryParseServices(args[4], out var services)
                    || (services & ~WFShipServiceKind.All) != 0
                    || !float.TryParse(args[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var multiplier)
                    || !float.IsFinite(multiplier) || multiplier is < 0 or > 10)
                {
                    shell.WriteError(Loc.GetString("cmd-wf_shipprice-bad-rule"));
                    return;
                }

                _services.SetRule(ship, new WFShipPriceRule
                {
                    Name = args[2],
                    Audience = audience,
                    Company = company,
                    Services = services,
                    Multiplier = multiplier,
                }, shell.Player?.AttachedEntity);
                shell.WriteLine(Loc.GetString("cmd-wf_shipprice-added", ("name", args[2])));
                break;
            case "toggle" when args.Length == 3:
                if (!_services.ToggleRule(ship, args[2], shell.Player?.AttachedEntity, out var enabled))
                {
                    shell.WriteError(Loc.GetString("cmd-wf_shipprice-no-rule", ("name", args[2])));
                    return;
                }

                shell.WriteLine(Loc.GetString("cmd-wf_shipprice-toggled", ("name", args[2]), ("enabled", enabled)));
                break;
            case "remove" when args.Length == 3:
                if (!_services.RemoveRule(ship, args[2], shell.Player?.AttachedEntity))
                {
                    shell.WriteError(Loc.GetString("cmd-wf_shipprice-no-rule", ("name", args[2])));
                    return;
                }

                shell.WriteLine(Loc.GetString("cmd-wf_shipprice-removed", ("name", args[2])));
                break;
            default:
                shell.WriteLine(Help);
                break;
        }
    }

    private static bool TryParseAudience(string arg, out WFShipPriceAudience audience, out string? company)
    {
        company = null;
        if (arg.StartsWith("company:", StringComparison.OrdinalIgnoreCase) && arg.Length > "company:".Length)
        {
            audience = WFShipPriceAudience.Company;
            company = arg["company:".Length..];
            return true;
        }

        return Enum.TryParse(arg, true, out audience)
               && audience is WFShipPriceAudience.Everyone or WFShipPriceAudience.Staff or WFShipPriceAudience.Officers;
    }

    private static bool TryParseServices(string arg, out WFShipServiceKind services)
    {
        services = WFShipServiceKind.None;
        foreach (var part in arg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Enum.TryParse<WFShipServiceKind>(part, true, out var kind) || kind == WFShipServiceKind.None)
                return false;

            services |= kind;
        }

        return services != WFShipServiceKind.None;
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(new[] { "list", "add", "toggle", "remove" }, Loc.GetString("cmd-wf_shipprice-hint-sub")),
            2 => CompletionResult.FromHintOptions(new[] { "here" }, Loc.GetString("cmd-wf_ship-hint-grid")),
            3 when args[0] == "toggle" => CompletionResult.FromHintOptions(new[] { "owner", "admins" }, Loc.GetString("cmd-wf_shipprice-hint-name")),
            3 => CompletionResult.FromHint(Loc.GetString("cmd-wf_shipprice-hint-name")),
            4 when args[0] == "add" => CompletionResult.FromHintOptions(new[] { "everyone", "staff", "officers", "company:" }, Loc.GetString("cmd-wf_shipprice-hint-audience")),
            5 when args[0] == "add" => CompletionResult.FromHintOptions(
                Enum.GetNames<WFShipServiceKind>().Where(n => n != nameof(WFShipServiceKind.None)).Select(n => n.ToLowerInvariant()),
                Loc.GetString("cmd-wf_shipprice-hint-services")),
            6 when args[0] == "add" => CompletionResult.FromHint(Loc.GetString("cmd-wf_shipprice-hint-multiplier")),
            _ => CompletionResult.Empty,
        };
    }
}
