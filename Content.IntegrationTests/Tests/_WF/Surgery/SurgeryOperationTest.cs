#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests._WF.Wolfmed;
using Content.Server._WF.Surgery;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Medical.Surgery.Steps.Parts;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Surgery;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Standing;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._WF.Surgery;

/// <summary>
/// The state-driven surgery engine: anatomical state, state-gated availability and server-authoritative
/// execution of an operation.
/// </summary>
[TestFixture]
[TestOf(typeof(SurgicalOperationSystem))]
public sealed class SurgeryOperationTest : WolfmedGameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private SurgicalSiteSystem Sites => SEntMan.System<SurgicalSiteSystem>();
    private SurgicalOperationSystem Surgery => SEntMan.System<SurgicalOperationSystem>();
    private SharedBodySystem Body => SEntMan.System<SharedBodySystem>();
    private StandingStateSystem Standing => SEntMan.System<StandingStateSystem>();
    private SharedHandsSystem Hands => SEntMan.System<SharedHandsSystem>();

    private async Task<(EntityUid Patient, EntityUid Torso)> SpawnPatient()
    {
        var map = await CreateTestMap();
        EntityUid patient = default;
        EntityUid torso = default;

        await Server.WaitAssertion(() =>
        {
            patient = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            torso = Body.GetBodyChildrenOfType(patient, BodyPartType.Torso).First().Id;
            Standing.Down(patient, playSound: false, dropHeldItems: false);
        });

        return (patient, torso);
    }

    private async Task<(EntityUid Surgeon, EntityUid Patient, EntityUid Torso)> SpawnPair()
    {
        var (patient, torso) = await SpawnPatient();
        EntityUid surgeon = default;

        await Server.WaitAssertion(() =>
        {
            surgeon = SEntMan.SpawnEntity("MobHuman", SEntMan.GetComponent<TransformComponent>(patient).Coordinates);
            if (SEntMan.TryGetComponent<TargetingComponent>(surgeon, out var targeting))
                targeting.Target = TargetBodyPart.Torso;
        });

        return (surgeon, patient, torso);
    }

    [Test]
    public async Task StateFollowsMarkers()
    {
        var (_, torso) = await SpawnPatient();

        await Server.WaitAssertion(() =>
        {
            Assert.That(Sites.GetState(torso), Is.EqualTo(SurgeryState.None));

            SEntMan.EnsureComponent<IncisionOpenComponent>(torso);
            Sites.Recalculate(torso);
            Assert.That(Sites.GetState(torso).HasAll(SurgeryState.SkinCut), Is.True);
            Assert.That(Sites.GetState(torso).HasAll(SurgeryState.VesselsUnclamped), Is.True,
                "Cutting the skin exposes unclamped vessels.");

            SEntMan.EnsureComponent<SkinRetractedComponent>(torso);
            Sites.Recalculate(torso);
            Assert.That(Sites.GetState(torso).HasAny(SurgeryState.SkinOpen), Is.True);
            Assert.That(Sites.GetState(torso).HasAny(SurgeryState.SkinCut), Is.False,
                "Retracting the skin replaces the cut state.");

            SEntMan.EnsureComponent<BleedersClampedComponent>(torso);
            Sites.Recalculate(torso);
            Assert.That(Sites.GetState(torso).HasAll(SurgeryState.VesselsClamped), Is.True);
            Assert.That(Sites.GetState(torso).HasAny(SurgeryState.VesselsUnclamped), Is.False);
        });
    }

    [Test]
    public async Task AvailabilityIsStateGated()
    {
        var (surgeon, patient, torso) = await SpawnPair();
        EntityUid scalpel = default;
        EntityUid retractor = default;

        await Server.WaitAssertion(() =>
        {
            scalpel = SEntMan.SpawnEntity("Scalpel", SEntMan.GetComponent<TransformComponent>(patient).Coordinates);
            retractor = SEntMan.SpawnEntity("Retractor", SEntMan.GetComponent<TransformComponent>(patient).Coordinates);

            var options = Surgery.GetAvailableOperations(surgeon, patient, scalpel);
            Assert.That(options.Any(option => option.Operation.ID == "WFInciseSkin"), Is.True,
                "An intact torso should offer the incision.");

            var wrongTool = Surgery.GetAvailableOperations(surgeon, patient, null);
            Assert.That(wrongTool.Any(option => option.Operation.ID == "WFInciseSkin"), Is.False,
                "A bare hand is not a cutting tool.");
        });
    }

    [Test]
    public async Task IncisionExecutesAndEnablesRetraction()
    {
        var (surgeon, patient, torso) = await SpawnPair();
        EntityUid scalpel = default;
        EntityUid retractor = default;

        await Server.WaitAssertion(() =>
        {
            var coords = SEntMan.GetComponent<TransformComponent>(patient).Coordinates;
            scalpel = SEntMan.SpawnEntity("Scalpel", coords);
            retractor = SEntMan.SpawnEntity("Retractor", coords);
            Hands.TryPickupAnyHand(surgeon, scalpel);

            var option = Surgery.GetAvailableOperations(surgeon, patient, scalpel)
                .First(option => option.Operation.ID == "WFInciseSkin");
            Surgery.StartOperation(surgeon, patient, scalpel, option);
            Assert.That(SEntMan.HasComponent<Content.Shared.DoAfter.ActiveDoAfterComponent>(surgeon), Is.True,
                "The incision should have started a do-after.");
        });

        await RunSeconds(6);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<IncisionOpenComponent>(torso), Is.True,
                "The completed incision should have opened the skin.");

            var options = Surgery.GetAvailableOperations(surgeon, patient, retractor);
            Assert.That(options.Any(option => option.Operation.ID == "WFRetractSkin"), Is.True,
                "Once the skin is cut, a retractor should offer retraction.");
            Assert.That(options.Any(option => option.Operation.ID == "WFInciseSkin"), Is.False,
                "A cut torso should not offer another incision.");
        });
    }

    [Test]
    public async Task ClosingClearsState()
    {
        var (surgeon, patient, torso) = await SpawnPair();
        EntityUid cautery = default;

        await Server.WaitAssertion(() =>
        {
            var coords = SEntMan.GetComponent<TransformComponent>(patient).Coordinates;
            cautery = SEntMan.SpawnEntity("Cautery", coords);
            Hands.TryPickupAnyHand(surgeon, cautery);

            SEntMan.EnsureComponent<IncisionOpenComponent>(torso);
            SEntMan.EnsureComponent<SkinRetractedComponent>(torso);
            SEntMan.EnsureComponent<BleedersClampedComponent>(torso);
            Sites.Recalculate(torso);

            var option = Surgery.GetAvailableOperations(surgeon, patient, cautery)
                .First(option => option.Operation.ID == "WFMendSkin");
            Surgery.StartOperation(surgeon, patient, cautery, option);
        });

        await RunSeconds(6);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Sites.GetState(torso), Is.EqualTo(SurgeryState.None),
                "Mending the incision should clear every state it opened.");
        });
    }

    [Test]
    public async Task MendBoneClearsFracture()
    {
        var (surgeon, patient, torso) = await SpawnPair();
        EntityUid boneGel = default;
        EntityUid? fracture = null;

        await Server.WaitAssertion(() =>
        {
            var coords = SEntMan.GetComponent<TransformComponent>(patient).Coordinates;
            boneGel = SEntMan.SpawnEntity("BoneGel", coords);
            Hands.TryPickupAnyHand(surgeon, boneGel);

            SEntMan.EnsureComponent<IncisionOpenComponent>(torso);
            SEntMan.EnsureComponent<SkinRetractedComponent>(torso);
            SEntMan.EnsureComponent<BleedersClampedComponent>(torso);
            SEntMan.EnsureComponent<RibcageSawedComponent>(torso);
            Sites.Recalculate(torso);

            // Sawing the ribs leaves a fracture behind, as the real wound system does.
            var wounds = SEntMan.System<WoundSystem>();
            var wound = wounds.CreateOrMergeWound(torso, "BoneFractureWound", 20);
            Assert.That(wound, Is.Not.Null, "the test could not create a fracture wound.");
            var fractureComp = SEntMan.EnsureComponent<WoundFractureComponent>(wound!.Value);
            fractureComp.Grade = FractureGrade.Comminuted;
            fracture = wound;

            var option = Surgery.GetAvailableOperations(surgeon, patient, boneGel)
                .First(option => option.Operation.ID == "WFFixBones");
            Surgery.StartOperation(surgeon, patient, boneGel, option);
        });

        await RunSeconds(8);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<RibcageSawedComponent>(torso), Is.False,
                "Mending the bone should close the sawed ribcage.");
            Assert.That(fracture is { } remaining && SEntMan.EntityExists(remaining), Is.False,
                "Mending the bone should also mend the fracture it left behind.");
        });
    }

    [Test]
    public async Task DuplicateAndCavityOperationsAreGone()
    {
        var prototypes = Server.ResolveDependency<Robust.Shared.Prototypes.IPrototypeManager>();

        await Server.WaitAssertion(() =>
        {
            Assert.That(prototypes.HasIndex<SurgicalOperationPrototype>("WFWidenCavity"), Is.False,
                "cavity widening is not part of the current surgery set.");
            Assert.That(prototypes.HasIndex<SurgicalOperationPrototype>("WFCloseCavity"), Is.False,
                "cavity closing is not part of the current surgery set.");

            var duplicateSteps = new[]
            {
                "SurgeryStepSealOrganWound",
                "SurgeryStepSealWounds",
                "SurgeryStepInsertOrgan",
                "SurgeryStepRemoveOrgan",
                "SurgeryStepInsertHeart",
                "SurgeryStepInsertEyes",
                "SurgeryStepInsertLiver",
                "SurgeryStepInsertLungs",
                "SurgeryStepInsertStomach",
                "WFSurgeryStepInsertKidneys",
            };

            foreach (var operation in prototypes.EnumeratePrototypes<SurgicalOperationPrototype>())
            {
                // The generic organ manipulation legitimately runs the shared remove-organ step; only the
                // generated per-surgery operations were duplicates.
                if (!operation.ID.StartsWith("WFOp", StringComparison.Ordinal) || operation.Step is not { } step)
                    continue;

                Assert.That(duplicateSteps, Has.None.EqualTo(step.ToString()),
                    $"{operation.ID} reintroduces a per-surgery duplicate of {step}.");
            }
        });
    }
}
