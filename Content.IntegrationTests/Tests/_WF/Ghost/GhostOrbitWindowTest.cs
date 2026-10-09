#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Content.Client._WF.Ghost;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._WF.Ghost;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Ghost;

public sealed class GhostOrbitWindowTest : InteractionTest
{
    /// <summary>A click on a section's header folds it, another opens it again, and a refresh keeps it as it was left.</summary>
    [Test]
    public async Task SectionsFoldAndStayFolded()
    {
        GhostOrbitWindow window = default!;
        var receive = typeof(GhostOrbitWindow).GetMethod("OnTargetsReceived", BindingFlags.Instance | BindingFlags.NonPublic)!;
        List<GhostOrbitTarget> Targets(int followers) => new()
        {
            new GhostOrbitTarget { Entity = Player, Name = "Somebody", Category = GhostOrbitCategory.Alive, Followers = followers },
            new GhostOrbitTarget { Entity = Player, Name = "A ship", Category = GhostOrbitCategory.Ship },
        };

        await Client.WaitPost(() =>
        {
            window = new GhostOrbitWindow();
            window.OpenCentered();
            receive.Invoke(window, new object[] { new GhostOrbitTargetsEvent(Targets(0)) });
        });
        await RunTicks(5);

        (ContainerButton Header, Control Body) Section(int index)
        {
            var root = window.FindControl<BoxContainer>("Sections").GetChild(index);
            return ((ContainerButton) root.GetChild(0), root.GetChild(1));
        }

        await Client.WaitAssertion(() => Assert.That(Section(0).Body.Visible, Is.True));
        await ClickControl(Section(0).Header);
        await RunTicks(2);
        await Client.WaitAssertion(() => Assert.That(Section(0).Body.Visible, Is.False, "A click folds the section."));

        await Client.WaitPost(() => receive.Invoke(window, new object[] { new GhostOrbitTargetsEvent(Targets(1)) }));
        await RunTicks(2);
        await Client.WaitAssertion(() => Assert.That(Section(0).Body.Visible, Is.False, "A refresh leaves it folded."));

        await ClickControl(Section(0).Header);
        await RunTicks(2);
        await Client.WaitAssertion(() =>
        {
            Assert.That(Section(0).Body.Visible, Is.True, "Another click opens it.");
            window.Close();
        });
    }
}
