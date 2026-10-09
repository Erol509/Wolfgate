#nullable enable
using System;
using System.Linq;
using System.Numerics;
using Content.Server._WF.NpcCrew;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Shared._WF.NpcCrew;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.NpcCrew;

public sealed partial class WFCrewTest
{
    /// <summary>Each skill level is no worse than the one below it, and a mission's skill reaches every crewman.</summary>
    [Test]
    public async Task CrewSkillScalesAndReachesTheCrew()
    {
        var levels = Enum.GetValues<WFCrewSkill>().Select(WFCrewSkills.Of).ToArray();
        for (var i = 1; i < levels.Length; i++)
        {
            Assert.That(levels[i].AimError, Is.LessThanOrEqualTo(levels[i - 1].AimError));
            Assert.That(levels[i].ShootDelay, Is.LessThanOrEqualTo(levels[i - 1].ShootDelay));
            Assert.That(levels[i].GunneryError, Is.LessThanOrEqualTo(levels[i - 1].GunneryError));
            Assert.That(levels[i].Leading, Is.GreaterThanOrEqualTo(levels[i - 1].Leading));
            Assert.That(levels[i].Handling, Is.GreaterThanOrEqualTo(levels[i - 1].Handling));
            Assert.That(levels[i].Evasion, Is.GreaterThanOrEqualTo(levels[i - 1].Evasion));
            Assert.That(levels[i].DamageTaken, Is.LessThanOrEqualTo(levels[i - 1].DamageTaken));
            Assert.That(levels[i].DamageDealt, Is.GreaterThanOrEqualTo(levels[i - 1].DamageDealt));
        }

        Assert.That(WFCrewSkills.Of(WFCrewSkill.Green).DodgesFire, Is.False);
        Assert.That(WFCrewSkills.Of(WFCrewSkill.Elite).AimError, Is.Zero);

        var deck = await CreateDeck(new Vector2(6, 0), 5, gravity: true);
        await Server.WaitAssertion(() =>
        {
            var hand = Server.System<WFCrewSystem>().SpawnCrewman(WFCrewRoles.Deckhand,
                new Robust.Shared.Map.EntityCoordinates(deck, new Vector2(2.5f)), "skill")!.Value;
            SEntMan.GetComponent<Content.Server.NPC.HTN.HTNComponent>(hand).Enabled = false;
            Assert.That(SEntMan.GetComponent<WFCrewComponent>(hand).Skill, Is.EqualTo(WFCrewSkill.Veteran));
            Server.System<WFCrewSetupSystem>().ApplyMission(hand, deck, new WFCrewMission { Group = "skill", Skill = WFCrewSkill.Green });
            Assert.That(SEntMan.GetComponent<WFCrewComponent>(hand).Skill, Is.EqualTo(WFCrewSkill.Green));
        });
    }

    /// <summary>The same hit hurts a green crewman more than an elite one.</summary>
    [Test]
    public async Task GreenCrewTakeMoreDamageThanElite()
    {
        var deck = await CreateDeck(new Vector2(6, 0), 5, gravity: true);
        await Server.WaitAssertion(() =>
        {
            var crew = Server.System<WFCrewSystem>();
            var damageable = Server.System<Content.Shared.Damage.DamageableSystem>();
            Content.Shared.FixedPoint.FixedPoint2 Hit(WFCrewSkill skill, float x)
            {
                var hand = crew.SpawnCrewman(WFCrewRoles.Deckhand, new Robust.Shared.Map.EntityCoordinates(deck, new Vector2(x, 2.5f)), "frail")!.Value;
                SEntMan.GetComponent<Content.Server.NPC.HTN.HTNComponent>(hand).Enabled = false;
                SEntMan.GetComponent<WFCrewComponent>(hand).Skill = skill;
                var before = SEntMan.GetComponent<Content.Shared.Damage.DamageableComponent>(hand).TotalDamage;
                var blow = new Content.Shared.Damage.DamageSpecifier { DamageDict = { ["Blunt"] = 10 } };
                damageable.TryChangeDamage(hand, blow, ignoreResistances: true);
                return SEntMan.GetComponent<Content.Shared.Damage.DamageableComponent>(hand).TotalDamage - before;
            }

            var elite = Hit(WFCrewSkill.Elite, 1.5f);
            var green = Hit(WFCrewSkill.Green, 3.5f);
            Assert.That(elite, Is.GreaterThan(Content.Shared.FixedPoint.FixedPoint2.Zero), "The blow has to land at all.");
            Assert.That(green, Is.GreaterThan(elite), $"green took {green}, elite took {elite}");
        });
    }
}
