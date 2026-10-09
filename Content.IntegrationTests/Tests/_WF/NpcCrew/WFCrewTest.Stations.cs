#nullable enable
using System.Numerics;
using Content.Server._Mono.NPC.HTN;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.Damage.Systems;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.Shuttles.Components;
using Content.Shared._Mono.CCVar;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Damage;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.NpcCrew;

public sealed partial class WFCrewTest
{
    /// <summary>Station crew return after displacement and defend only against their own living attacker.</summary>
    [TestCase("WFCrewPilot", true)]
    [TestCase("WFCrewGunner", false)]
    [TestCase("WFCrewCaptain", true)]
    [TestCase("WFCrewRadioOperator", false)]
    public async Task StationCrewReturnAndLimitRetaliation(string role, bool killAttacker)
    {
        var config = Client.ResolveDependency<IConfigurationManager>();
        var echo = config.GetCVar(MonoCVars.AreaEchoEnabled);
        await Client.WaitPost(() => config.SetCVar(MonoCVars.AreaEchoEnabled, false));
        EntityUid member = default;
        try
        {
            var deck = await CreateDeck(new Vector2(6, 0), 13, gravity: true);
            var post = new EntityCoordinates(deck, new Vector2(2.5f, 4.5f));
            var destination = new EntityCoordinates(MapData.MapUid, new Vector2(500, 500));
            EntityUid console = default, attacker = default, bystander = default;
            var pilotRole = role == "WFCrewPilot";
            var gunnerRole = role == "WFCrewGunner";
            await Server.WaitPost(() =>
            {
                if (pilotRole)
                    SEntMan.EnsureComponent<ShuttleComponent>(deck);
                if (pilotRole || gunnerRole)
                    console = SEntMan.SpawnAtPosition(pilotRole ? TestHelm : "WFTestGunnery",
                        new EntityCoordinates(deck, new Vector2(3.5f, 4.5f)));
                var crew = Server.System<WFCrewSystem>();
                member = crew.SpawnCrewman(role, post, "stations")!.Value;
                crew.SetEngagement(member, WFCrewEngagement.OnSight);
                SEntMan.EnsureComponent<WFCrewSecurityComponent>(member).Boarding = WFCrewSecurityResponse.Hostile;
                attacker = SEntMan.SpawnAtPosition(Hostile, new EntityCoordinates(deck, new Vector2(2.5f, 6.5f)));
                bystander = SEntMan.SpawnAtPosition(Hostile, new EntityCoordinates(deck, new Vector2(1.5f, 8.5f)));
                Server.System<GodmodeSystem>().EnableGodmode(attacker);
                Server.System<GodmodeSystem>().EnableGodmode(bystander);
                if (pilotRole)
                    Server.System<WFPilotDutySystem>().GoTo(member, new() { destination });
            });

            bool AtStation()
            {
                var position = SEntMan.GetComponent<TransformComponent>(member).Coordinates;
                if (pilotRole)
                    return SEntMan.GetComponent<WFPilotDutyComponent>(member).AtHelm
                        && position.InRange(SEntMan, SEntMan.GetComponent<TransformComponent>(console).Coordinates, 2f);
                if (gunnerRole)
                    return SEntMan.GetComponent<WFGunnerDutyComponent>(member).AtConsole
                        && position.InRange(SEntMan, SEntMan.GetComponent<TransformComponent>(console).Coordinates, 2f);
                return position.InRange(SEntMan, post, 1.6f);
            }

            await WaitUntil(AtStation, 600, () => Describe(member));
            await RunTicks(100);
            await Server.WaitAssertion(() =>
            {
                var weapons = Server.System<WFCrewWeaponSystem>();
                Assert.That(weapons.CanSee(member, attacker), Is.True);
                Assert.That(Server.System<WFCrewSecuritySystem>().HasThreat(member), Is.True);
                Assert.That(weapons.CanEngage(member, attacker), Is.False, "Boarding rules and OnSight must not pull operators away.");
                Assert.That(Server.System<SharedHandsSystem>().TryGetActiveItem(member, out _), Is.False);
                Assert.That(AtStation(), Is.True);
                Server.System<SharedTransformSystem>().SetCoordinates(member,
                    new EntityCoordinates(deck, new Vector2(10.5f, 4.5f)));
                Assert.That(AtStation(), Is.False);
            });
            await WaitUntil(AtStation, 900, () => $"Displaced {role} did not walk back: {Describe(member)}");
            await Server.WaitAssertion(() =>
            {
                if (pilotRole)
                    Assert.That(SEntMan.GetComponent<WFPilotDutyComponent>(member).Console, Is.EqualTo(console));
                var damage = new DamageSpecifier { DamageDict = { ["Blunt"] = 10 } };
                Server.System<DamageableSystem>().TryChangeDamage(member, damage, ignoreResistances: true, origin: attacker);
            });
            await WaitUntil(() => (SEntMan.HasComponent<NPCRangedCombatComponent>(member)
                                    || SEntMan.HasComponent<NPCMeleeCombatComponent>(member))
                                && Server.System<SharedHandsSystem>().TryGetActiveItem(member, out var held)
                                && SEntMan.HasComponent<GunComponent>(held.Value),
                600, () => $"Personally attacked {role} did not defend itself: {Describe(member)}");
            await Server.WaitAssertion(() =>
            {
                Assert.That(Server.System<WFCrewWeaponSystem>().CanEngage(member, attacker), Is.True);
                Assert.That(Server.System<WFCrewWeaponSystem>().CanEngage(member, bystander), Is.False);
                if (killAttacker)
                    Server.System<MobStateSystem>().ChangeMobState(attacker, MobState.Dead);
                else
                    SEntMan.DeleteEntity(attacker);
            });
            await WaitUntil(() => AtStation()
                                  && !SEntMan.HasComponent<NPCRangedCombatComponent>(member)
                                  && !SEntMan.HasComponent<NPCMeleeCombatComponent>(member)
                                  && !Server.System<SharedHandsSystem>().TryGetActiveItem(member, out _),
                900, () => $"{role} did not resume its station with another boarder still aboard: {Describe(member)}");
            await Server.WaitAssertion(() =>
            {
                Assert.That(Server.System<MobStateSystem>().IsAlive(bystander), Is.True);
                Assert.That(Server.System<WFCrewWeaponSystem>().CanEngage(member, bystander), Is.False);
                if (pilotRole)
                {
                    // With a boarder still aboard and no captain, the pilot may be holding the ship; his course is
                    // then saved, not lost. Otherwise he is still flying it.
                    var duty = SEntMan.GetComponent<WFPilotDutyComponent>(member);
                    if (Server.System<WFCaptainSystem>().IsCourseSuspended(member))
                    {
                        Assert.That(duty.Orders, Is.EqualTo(WFPilotOrder.Hold));
                        Assert.That(Server.System<WFCrewAlertSystem>().IsAlerted(deck, "stations"), Is.True);
                    }
                    else
                    {
                        Assert.That(duty.Orders, Is.EqualTo(WFPilotOrder.GoTo));
                        Assert.That(duty.Waypoints, Is.EqualTo(new[] { destination }));
                        Assert.That(SEntMan.GetComponent<ShipSteererComponent>(member).Coordinates, Is.EqualTo(destination));
                    }
                }
            });
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                if (SEntMan.TryGetComponent<HTNComponent>(member, out var htn))
                    htn.Enabled = false;
            });
            await RunTicks(90);
            await Client.WaitPost(() => config.SetCVar(MonoCVars.AreaEchoEnabled, echo));
        }
    }
}
