#nullable enable
using Content.Shared._WF.BorgSkins;
using Content.Shared.Actions;
using Content.Shared.Silicons.Borgs;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.BorgSkins;

/// <summary>A borg gets an action for each pose its skin lists, and the actions toggle the pose it holds.</summary>
[TestFixture]
[TestOf(typeof(WFBorgPoseSystem))]
public sealed class WFBorgPoseTest
{
    private const string Borg = "BorgChassisSelectable";
    private const string PosedSkin = "WFBorgSkinEngineeringC9T7O";
    private const string PlainSkin = "generic_default";

    [Test]
    public async Task SkinGrantsPoseActions()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entMan = server.EntMan;
        var prototypes = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var borg = entMan.SpawnEntity(Borg, map.GridCoords);
            Assert.That(entMan.HasComponent<WFBorgPoseComponent>(borg), Is.False, "A borg with no skin has poses.");

            var selected = new WFBorgSubtypeSelectedEvent(PosedSkin);
            entMan.EventBus.RaiseLocalEvent(borg, ref selected);

            var offered = prototypes.Index<WFBorgSkinPrototype>(PosedSkin).Poses;
            Assert.That(offered, Is.Not.Empty);
            Assert.That(entMan.TryGetComponent(borg, out WFBorgPoseComponent? pose), "The skin granted no poses.");
            Assert.That(pose!.Actions, Has.Count.EqualTo(offered.Count), "The skin did not grant an action per pose.");
            foreach (var action in pose.Actions)
            {
                Assert.That(entMan.HasComponent<InstantActionComponent>(action));
            }

            var rest = new WFBorgPoseActionEvent { Pose = WFBorgPose.Rest, Performer = borg };
            entMan.EventBus.RaiseLocalEvent(borg, rest);
            Assert.That(pose.Pose, Is.EqualTo(WFBorgPose.Rest), "The action did not strike the pose.");

            var sit = new WFBorgPoseActionEvent { Pose = WFBorgPose.Sit, Performer = borg };
            entMan.EventBus.RaiseLocalEvent(borg, sit);
            Assert.That(pose.Pose, Is.EqualTo(WFBorgPose.Sit), "A second pose did not replace the first.");

            sit = new WFBorgPoseActionEvent { Pose = WFBorgPose.Sit, Performer = borg };
            entMan.EventBus.RaiseLocalEvent(borg, sit);
            Assert.That(pose.Pose, Is.EqualTo(WFBorgPose.None), "Using a held pose's action did not drop it.");

            var actions = pose.Actions.ToArray();
            selected = new WFBorgSubtypeSelectedEvent(PlainSkin);
            entMan.EventBus.RaiseLocalEvent(borg, ref selected);
            Assert.That(entMan.HasComponent<WFBorgPoseComponent>(borg), Is.False, "A plain skin kept the poses.");
            foreach (var action in actions)
            {
                Assert.That(entMan.Deleted(action) || entMan.IsQueuedForDeletion(action), "A pose action outlived its skin.");
            }
        });

        await pair.CleanReturnAsync();
    }
}
