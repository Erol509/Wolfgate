using System.Collections.Generic;
using Content.Server._WF.WolfChem.Narcotics;
using Content.Shared._WF.WolfChem.Narcotics;
using Content.Shared.Drunk;
using Content.Shared.Inventory;
using Content.Shared.StatusEffect;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;

namespace Content.IntegrationTests.Tests._WF.WolfChem.Narcotics;

/// <summary>
/// Examine cues: a light high shows on an uncovered face up close, a heavy one in how the body moves at any distance,
/// withdrawal and drink show too, and every line exists.
/// </summary>
[TestFixture]
[TestOf(typeof(WFDrugCuesSystem))]
public sealed class DrugCuesTest
{
    [Test]
    public async Task CuesFollowPhaseAndFaceTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var cues = server.System<WFDrugCuesSystem>();
            var mob = entMan.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(cues.GetCues(mob, true), Is.EqualTo(((string) null, (string) null)), "A sober body shows nothing.");

            var comp = entMan.EnsureComponent<WFIntoxicationComponent>(mob);
            comp.Levels["WFHeroin"] = 2f;
            Assert.That(cues.GetCues(mob, true), Is.EqualTo(("wf-drug-cue-opioid-face", (string) null)),
                "A light high shows on the face only.");
            Assert.That(cues.GetCues(mob, false), Is.EqualTo(((string) null, (string) null)),
                "A light high doesn't show from a distance.");

            comp.Levels["WFHeroin"] = 10f;
            Assert.That(cues.GetCues(mob, true), Is.EqualTo(("wf-drug-cue-opioid-face", "wf-drug-cue-opioid-body-heavy")));
            Assert.That(cues.GetCues(mob, false), Is.EqualTo(((string) null, "wf-drug-cue-opioid-body-heavy")),
                "A heavy high shows in the body at any distance.");

            comp.Levels["WFHeroin"] = 20f;
            Assert.That(cues.GetCues(mob, true).Body, Is.EqualTo("wf-drug-cue-opioid-body-overdose"));

            // The stronger of two highs speaks.
            comp.Levels["WFHeroin"] = 2f;
            comp.Levels["WFCrystal"] = 10f;
            Assert.That(cues.GetCues(mob, true), Is.EqualTo(("wf-drug-cue-stimulant-face", "wf-drug-cue-stimulant-body-heavy")));

            var glasses = entMan.SpawnEntity("ClothingEyesGlassesSunglasses", map.GridCoords);
            Assert.That(server.System<InventorySystem>().TryEquip(mob, glasses, "eyes", silent: true, force: true), Is.True);
            Assert.That(cues.GetCues(mob, true), Is.EqualTo(((string) null, "wf-drug-cue-stimulant-body-heavy")),
                "Covered eyes hide the face line, not the body's.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WithdrawalAndDrinkShowTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var cues = server.System<WFDrugCuesSystem>();
            var mob = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var dependence = entMan.EnsureComponent<WFDependenceComponent>(mob);
            dependence.Stage[WFDrugFamily.Opioid] = 1;
            Assert.That(cues.GetCues(mob, true), Is.EqualTo(((string) null, (string) null)), "Cravings don't show.");

            dependence.Stage[WFDrugFamily.Opioid] = 2;
            Assert.That(cues.GetCues(mob, true), Is.EqualTo(((string) null, "wf-drug-cue-withdrawal-body-2")));

            dependence.Stage[WFDrugFamily.Opioid] = 3;
            Assert.That(cues.GetCues(mob, true), Is.EqualTo(("wf-drug-cue-withdrawal-face", "wf-drug-cue-withdrawal-body-3")));

            var drinker = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var status = server.System<StatusEffectsSystem>();
            Assert.That(status.TryAddStatusEffect<DrunkComponent>(drinker, SharedDrunkSystem.DrunkKey,
                TimeSpan.FromSeconds(WFAlcoholSystem.LightSeconds + 5f), true), Is.True);
            Assert.That(cues.GetCues(drinker, true), Is.EqualTo(("wf-drug-cue-drunk-face", (string) null)));

            status.TryAddStatusEffect<DrunkComponent>(drinker, SharedDrunkSystem.DrunkKey,
                TimeSpan.FromSeconds(WFAlcoholSystem.OverdoseSeconds + 5f), true);
            Assert.That(cues.GetCues(drinker, false), Is.EqualTo(((string) null, "wf-drug-cue-drunk-body-overdose")));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EveryLineExistsTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var loc = server.ResolveDependency<ILocalizationManager>();
            var keys = new List<string>
            {
                "wf-drug-cue-crash", "wf-drug-cue-withdrawal-face", "wf-drug-cue-withdrawal-body-2",
                "wf-drug-cue-withdrawal-body-3", "wf-drug-cue-synth-crash", "wf-drug-cue-synth-withdrawal",
            };
            foreach (var name in new[] { "stimulant", "opioid", "psychedelic", "empathogen", "cannabinoid", "combat", "drunk", "synth" })
            {
                keys.Add($"wf-drug-cue-{name}-face");
                keys.Add($"wf-drug-cue-{name}-body-heavy");
                keys.Add($"wf-drug-cue-{name}-body-overdose");
            }

            Assert.Multiple(() =>
            {
                foreach (var key in keys)
                {
                    Assert.That(loc.HasString(key), Is.True, key);
                }
            });
        });

        await pair.CleanReturnAsync();
    }
}
