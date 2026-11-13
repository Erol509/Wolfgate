using System.Linq;
using Content.Server._NF.SectorServices;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Server.Voting.Managers;
using Content.Server.Silicons.Borgs;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.Cyborgs;
using Content.Shared._WF.RoundSetup;
using Content.Shared._WF.ShipAccess;
using Content.Shared.CCVar;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mind;
using Content.Shared.Players;
using Content.Shared.Preferences;
using Content.Shared.Silicons.Borgs.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.RoundSetup;

public sealed partial class WFRoundSetupLobbyTest
{
    /// <summary>A host who picks the cyborg job spawns as a chassis with an issued card that holds the deed, and keeps that card when it picks a type.</summary>
    [Test]
    public async Task CyborgHostStartsShipCrew()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var server = pair.Server;
        var em = server.EntMan;
        var owner = pair.Player!;
        var ticker = server.System<GameTicker>();
        var preferences = server.ResolveDependency<IServerPreferencesManager>();
        var original = preferences.GetPreferences(owner.UserId);
        var slot = original.SelectedCharacterIndex;
        var profile = (HumanoidCharacterProfile) original.Characters[slot];
        var timers = server.CfgMan.GetCVar(CCVars.GameRoleTimers);
        var autoVote = server.CfgMan.GetCVar(CCVars.AutoVoteEnabled);
        var preset = ticker.Preset;
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.AutoVoteEnabled, false);
            server.CfgMan.SetCVar(CCVars.GameRoleTimers, false);
            foreach (var vote in server.ResolveDependency<IVoteManager>().ActiveVotes.ToArray())
                vote.Cancel();
        });
        await pair.RunTicksSync(2);
        try
        {
            await server.WaitPost(() =>
            {
                preferences.SetProfile(owner.UserId, slot,
                    HumanoidCharacterProfile.DefaultWithSpecies("Human").WithoutAllTraitPreferences().WithName("Ship Owner").WithBankBalance(10000000)).Wait();
                ticker.SetGamePreset("Sandbox");
                ticker.StartRound(force: true);
                if (!server.System<SectorServiceSystem>().GetServiceEntity().Valid)
                    em.AddComponent<StationSectorServiceHostComponent>(em.SpawnEntity(null, MapCoordinates.Nullspace));
            });
            var state = await Request(pair, new(WFRoundSetupAction.Create) { Scenario = WFRoundScenario.Ship });
            var id = OwnLobby(state).Id;
            Assert.That(state.Jobs.Select(job => job.Id), Does.Contain("Borg"), "A ship crew should offer the cyborg job.");
            await Request(pair, new(WFRoundSetupAction.SelectVessel) { LobbyId = id, Vessel = "Guppy" });
            await Request(pair, new(WFRoundSetupAction.SelectJob) { LobbyId = id, Job = "Borg" });
            state = await Request(pair, new(WFRoundSetupAction.ConfirmJoin) { LobbyId = id });
            Assert.That(OwnLobby(state).Started, Is.True, state.Message);
            EntityUid card = default;
            await server.WaitAssertion(() =>
            {
                var body = owner.AttachedEntity!.Value;
                Assert.That(em.HasComponent<BorgChassisComponent>(body), Is.True, "The cyborg job should spawn a chassis, not a humanoid.");
                var grid = em.GetComponent<TransformComponent>(body).GridUid!.Value;
                Assert.That(em.HasComponent<HandsComponent>(body) && server.System<SharedHandsSystem>().EnumerateHeld(body).Any(), Is.False,
                    "A chassis with no type picked should hold nothing.");
                Assert.That(server.System<WFShipAccessSystem>().TryGetWornCard(body, out card), Is.True, "The cyborg was issued no ID card.");
                Assert.That(em.TryGetComponent<ShuttleDeedComponent>(card, out var deed) && deed.ShuttleUid == grid, Is.True,
                    "The ship's deed should be on the cyborg's own card.");
                var access = em.GetComponent<WFShipAccessComponent>(grid);
                Assert.That(access.CrewId, Is.EqualTo(id));
                Assert.That(access.Mode, Is.EqualTo(WFShipAccessMode.Crew));
                Assert.That(access.CrewKeys.Count, Is.EqualTo(1), "The cyborg's card should be a crew credential.");
                Assert.That(server.System<WFShipAccessSystem>().IsOwner(body, grid), Is.True);
                // Picking a type fits the tool module, which takes the issued card over from the chassis.
                em.EventBus.RaiseLocalEvent(body, new BorgSelectTypeMessage("engineering", "engineer_default")
                {
                    UiKey = BorgSwitchableTypeUiKey.SelectBorgType,
                    Actor = body,
                });
            });
            await pair.RunTicksSync(5);
            await server.WaitAssertion(() =>
            {
                var body = owner.AttachedEntity!.Value;
                var cards = 0;
                var all = em.EntityQueryEnumerator<WFBorgIdCardComponent>();
                while (all.MoveNext(out _, out _))
                    cards++;
                Assert.That(cards, Is.EqualTo(1), "The tool module's blank card should give way to the issued one.");
                Assert.That(server.System<SharedHandsSystem>().EnumerateHeld(body), Does.Contain(card), "The issued card should be in the tool module's hand.");
                server.System<BorgSystem>().UnselectModule(body);
                Assert.That(server.System<WFBorgIdCardSystem>().TryGetCard(body, out var stowed) && stowed == card, Is.True,
                    "The issued card should stow with the tool module.");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                // A client still attached to a cyborg throws while the round's cleanup deletes it.
                server.System<SharedMindSystem>().WipeMind(owner.ContentData()?.Mind);
            });
            await pair.RunTicksSync(5);
            await server.WaitPost(() =>
            {
                ticker.RestartRound();
                ticker.SetGamePreset(preset);
                server.CfgMan.SetCVar(CCVars.GameRoleTimers, timers);
                preferences.SetProfile(owner.UserId, slot, profile).Wait();
                server.CfgMan.SetCVar(CCVars.AutoVoteEnabled, autoVote);
            });
        }
        await pair.RunTicksSync(2);
        await pair.CleanReturnAsync();
    }
}
