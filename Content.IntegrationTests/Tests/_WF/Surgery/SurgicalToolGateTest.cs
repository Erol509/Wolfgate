#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Tests._WF.Wolfmed;
using Content.Server._WF.Surgery;
using Content.Shared._WF.Surgery;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Standing;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._WF.Surgery;

[TestFixture]
public sealed class SurgicalToolGateTest : WolfmedGameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    [Test]
    public async Task HumanDoesNotOfferMechanicalOperations()
    {
        var map = await CreateTestMap();
        EntityUid patient = default;
        EntityUid surgeon = default;

        await Server.WaitAssertion(() =>
        {
            patient = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            surgeon = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            SEntMan.System<StandingStateSystem>().Down(patient, playSound: false, dropHeldItems: false);
            var torso = SEntMan.System<SharedBodySystem>()
                .GetBodyChildrenOfType(patient, BodyPartType.Torso).First().Id;

            var proto = SProtoMan.Index<SurgicalOperationPrototype>("WFUnscrewShell");
            Assert.That(proto.Flags.HasFlag(SurgicalOperationFlags.Mechanical), Is.True,
                $"the prototype flags did not parse as Mechanical: {proto.Flags}");
            Assert.That(proto.Tools.Count, Is.GreaterThan(0), "WFUnscrewShell tools did not parse.");
            var sawTools = SProtoMan.Index<SurgicalOperationPrototype>("WFSawBone").Tools;
            Assert.That(sawTools.Count, Is.GreaterThan(0), "WFSawBone tools did not parse.");
            Assert.That(sawTools[^1].AnyItem, Is.True, "saw anyItem did not parse.");
            Assert.That(sawTools[^1].RequiresSharp, Is.True, "saw requiresSharp did not parse.");
            Assert.That(sawTools[^1].MinForce, Is.EqualTo(10f), "saw minForce did not parse.");
            Assert.That(SProtoMan.Index<SurgicalOperationPrototype>("WFInciseSkin").Tools.Count, Is.GreaterThan(0),
                "WFInciseSkin tools did not parse.");

            var screwdriver = SEntMan.SpawnEntity("Screwdriver",
                SEntMan.GetComponent<TransformComponent>(patient).Coordinates);
            var options = SEntMan.System<SurgicalOperationSystem>()
                .GetAvailableOperations(surgeon, patient, screwdriver);
            Assert.That(options.Any(option => option.Operation.ID == "WFUnscrewShell"), Is.False,
                "mechanical unscrew offered on a human: " + string.Join(", ", options.Select(o => o.Operation.ID)));
            Assert.That(options.Any(option => option.Operation.ID == "WFInciseSkin"), Is.True,
                "plain incision not offered on a human.");
            Assert.That(options.Count, Is.EqualTo(1),
                "more than the incision was offered on an intact human: " +
                string.Join(", ", options.Select(o => o.Operation.ID)));

            SEntMan.EnsureComponent<Content.Shared._Shitmed.Medical.Surgery.Steps.Parts.IncisionOpenComponent>(torso);
            SEntMan.System<SurgicalSiteSystem>().Recalculate(torso);
            var retractor = SEntMan.SpawnEntity("Retractor",
                SEntMan.GetComponent<TransformComponent>(patient).Coordinates);
            var afterCut = SEntMan.System<SurgicalOperationSystem>()
                .GetAvailableOperations(surgeon, patient, retractor);
            Assert.That(afterCut.Count, Is.EqualTo(1),
                "more than retraction was offered after the cut: " +
                string.Join(", ", afterCut.Select(o => o.Operation.ID)));

            SEntMan.EnsureComponent<Content.Shared._Shitmed.Medical.Surgery.Steps.Parts.SkinRetractedComponent>(torso);
            SEntMan.System<SurgicalSiteSystem>().Recalculate(torso);
            var hemostat = SEntMan.SpawnEntity("Hemostat",
                SEntMan.GetComponent<TransformComponent>(patient).Coordinates);
            var hemostatComponents = string.Join(",",
                SEntMan.GetComponents(hemostat).Select(component => component.GetType().Name));
            Assert.That(SEntMan.System<SurgicalOperationSystem>().TryMatchTool(
                    SProtoMan.Index<SurgicalOperationPrototype>("WFSawBone"), hemostat, out _, out _), Is.False,
                "hemostat matched the saw operation: " + hemostatComponents);
            var afterRetract = SEntMan.System<SurgicalOperationSystem>()
                .GetAvailableOperations(surgeon, patient, hemostat);
            Assert.That(afterRetract.Count, Is.EqualTo(1),
                "more than clamping was offered after retraction: " +
                string.Join(", ", afterRetract.Select(o => o.Operation.ID)));
        });
    }
}
