using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Shared._WF.NpcCrew;
using Content.Shared._WF.ShipCommand;
using Content.Shared.Chat;
using Content.Shared.Database;
using Robust.Shared.Utility;

namespace Content.Server._WF.ShipCommand;

/// <summary>
/// A commanded ship's standing orders for its roster, set from the command console: where the marines stand, when the
/// gunners open fire, what the pilot does under attack, and the callsign on the air. Whoever is staffed later gets the
/// same orders.
/// </summary>
public sealed class WFShipBehaviourSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private WFCrewSystem _crew = default!;
    [Dependency] private WFRadioOperatorSystem _radio = default!;

    public const int MaxCallsign = 32;

    /// <summary>A callsign as it goes on the air: one short line with no markup.</summary>
    public static string CleanCallsign(string text)
    {
        return SharedChatSystem.SanitizeAnnouncement(FormattedMessage.RemoveMarkupPermissive(text), MaxCallsign)
            .Replace('\r', ' ').Replace('\n', ' ').Trim();
    }

    /// <summary>Changes whichever orders are given, admin-logged, and passes them to the crew aboard.</summary>
    public void Set(Entity<WFShipRosterComponent> grid, WFShipMarineOrders? marines, WFGunnerPolicy? gunners, WFShipPilotStance? pilot,
        string? callsign, EntityUid? actor)
    {
        var comp = grid.Comp;
        comp.MarineOrders = marines ?? comp.MarineOrders;
        comp.GunnerPolicy = gunners ?? comp.GunnerPolicy;
        comp.PilotStance = pilot ?? comp.PilotStance;
        if (callsign != null)
            comp.Callsign = CleanCallsign(callsign);

        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(actor):actor} set standing orders on {ToPrettyString(grid.Owner):grid}: marines {comp.MarineOrders}, gunners {comp.GunnerPolicy}, pilot {comp.PilotStance}, callsign '{comp.Callsign}'");
        ApplyAll(grid);
    }

    /// <summary>Passes the roster's orders to the ship's gunnery and everyone staffed on it.</summary>
    public void ApplyAll(Entity<WFShipRosterComponent> grid)
    {
        EnsureComp<WFGunnerPolicyComponent>(grid).Policy = grid.Comp.GunnerPolicy;
        foreach (var slot in grid.Comp.Slots.Values)
        {
            if (slot.Occupant is { } mob && !TerminatingOrDeleted(mob))
                ApplyTo(grid, mob);
        }
    }

    /// <summary>Passes the roster's orders to one crewman.</summary>
    public void ApplyTo(Entity<WFShipRosterComponent> grid, EntityUid mob)
    {
        if (TryComp<WFPilotDutyComponent>(mob, out var pilot))
        {
            var stance = grid.Comp.PilotStance;
            pilot.ReactToAttacks = stance is WFShipPilotStance.Evade or WFShipPilotStance.Run;
            pilot.FleeAttackers = stance == WFShipPilotStance.Run;
            pilot.FaceThreats = stance == WFShipPilotStance.FaceThreats;
        }

        if (TryComp<WFRadioOperatorComponent>(mob, out var radio))
            _radio.SetCallsign((mob, radio), grid.Comp.Callsign);

        if (TryComp<WFCrewComponent>(mob, out var crew) && crew.Role == WFCrewRoles.Marine)
            ApplyMarine(grid, mob, crew);
    }

    private void ApplyMarine(Entity<WFShipRosterComponent> grid, EntityUid mob, WFCrewComponent crew)
    {
        if (grid.Comp.MarineOrders == WFShipMarineOrders.Patrol)
        {
            RemComp<WFCrewPatrolOrdersComponent>(mob);
            return;
        }

        var orders = EnsureComp<WFCrewPatrolOrdersComponent>(mob);
        orders.Route.Clear();
        if (grid.Comp.MarineOrders == WFShipMarineOrders.Posts)
        {
            if (crew.Post is { } post)
                _crew.SetPost((mob, crew), post);
            return;
        }

        // Every marine walks the same round, each starting from his own post so they stay spread out.
        var slots = grid.Comp.Slots.Values
            .Where(s => s.Kind == WFShipRosterKind.Crew && s.Role == WFCrewRoles.Marine)
            .OrderBy(s => s.Id)
            .ToList();
        orders.Route.AddRange(slots.Select(s => s.Post));
        orders.Next = slots.FindIndex(s => s.Occupant == mob) + 1;
    }
}
