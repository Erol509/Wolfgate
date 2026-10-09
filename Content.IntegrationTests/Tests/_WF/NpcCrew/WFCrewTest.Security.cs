#nullable enable
using System.Numerics;
using System.Linq;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.NPC.HTN;
using Content.Shared._WF.NpcCrew;
using Content.Shared._WF.ShipAccess;
using Content.Shared.Access.Systems;
using Content.Shared.Damage;
using Content.Shared.NPC.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.NpcCrew;

public sealed partial class WFCrewTest
{
    /// <summary>Unregistered map ships receive ordinary crew credentials when populated.</summary>
    [Test]
    public async Task CrewEnrollsUnmanagedShip()
    {
        var deck = await CreateDeck(new Vector2(6, 0), 5, gravity: true);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<WFShipAccessComponent>(deck), Is.False);
            SEntMan.EnsureComponent<Content.Server.Shuttles.Components.ShuttleComponent>(deck);
            var door = SEntMan.SpawnAtPosition(TestDock, new EntityCoordinates(deck, new Vector2(3.5f)));
            var crew = Server.System<WFCrewSystem>().SpawnCrewman(WFCrewRoles.Deckhand,
                new EntityCoordinates(deck, new Vector2(2.5f)), "access")!.Value;
            SEntMan.GetComponent<HTNComponent>(crew).Enabled = false;
            Assert.That(SEntMan.GetComponent<WFShipAccessComponent>(deck).AllowList, Has.Count.EqualTo(1));
            Assert.That(Server.System<AccessReaderSystem>().IsAllowed(crew, door), Is.True);
        });
    }

    /// <summary>Friendly damage is cancelled before the wound router can apply it.</summary>
    [Test]
    public async Task CrewCannotDamageFriendlyCrew()
    {
        var deck = await CreateDeck(new Vector2(6, 0), 5, gravity: true);
        await Server.WaitAssertion(() =>
        {
            var crewSystem = Server.System<WFCrewSystem>();
            var one = crewSystem.SpawnCrewman(WFCrewRoles.Deckhand, new EntityCoordinates(deck, new Vector2(1.5f)), "crew")!.Value;
            var two = crewSystem.SpawnCrewman(WFCrewRoles.Deckhand, new EntityCoordinates(deck, new Vector2(2.5f)), "crew")!.Value;
            SEntMan.GetComponent<HTNComponent>(one).Enabled = false;
            SEntMan.GetComponent<HTNComponent>(two).Enabled = false;
            var before = SEntMan.GetComponent<DamageableComponent>(two).TotalDamage;
            var hit = new DamageSpecifier();
            hit.DamageDict.Add("Piercing", 20);
            Server.System<DamageableSystem>().TryChangeDamage(two, hit, origin: one);
            Assert.That(SEntMan.GetComponent<DamageableComponent>(two).TotalDamage, Is.EqualTo(before));
        });
    }

    /// <summary>Boarding rules distinguish a warning from explicit hostility.</summary>
    [TestCase(WFCrewSecurityResponse.Warn, false)]
    [TestCase(WFCrewSecurityResponse.Hostile, true)]
    public async Task CrewBoardingRules(WFCrewSecurityResponse response, bool hostile)
    {
        var deck = await CreateDeck(new Vector2(6, 0), 5, gravity: true);
        EntityUid crew = default;
        EntityUid visitor = default;
        await Server.WaitAssertion(() =>
        {
            crew = Server.System<WFCrewSystem>().SpawnCrewman(WFCrewRoles.Deckhand,
                new EntityCoordinates(deck, new Vector2(1.5f)), "crew")!.Value;
            visitor = Server.System<WFCrewSystem>().SpawnCrewman(WFCrewRoles.Deckhand,
                new EntityCoordinates(deck, new Vector2(2.5f)), "visitor")!.Value;
            SEntMan.GetComponent<HTNComponent>(crew).Enabled = false;
            SEntMan.GetComponent<HTNComponent>(visitor).Enabled = false;
            Server.System<NpcFactionSystem>().ClearFactions(visitor);
            SEntMan.EnsureComponent<WFCrewSecurityComponent>(crew).Boarding = response;
        });
        await RunTicks(70);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<WFCrewSecuritySystem>().IsAuthorized(crew, visitor), Is.False);
            Assert.That(SEntMan.TryGetComponent<Content.Shared.NPC.Components.FactionExceptionComponent>(crew, out var exceptions)
                && exceptions.Hostiles.Any(target => target == visitor), Is.EqualTo(hostile));
        });
    }
}
