using System.Numerics;
using Content.Client._WF.Modsuits;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Modsuits;

/// <summary>Ensures the MODsuit window XAML loads and lays out without runtime errors.</summary>
[TestFixture]
public sealed class ModSuitWindowTest
{
    [Test]
    public async Task WindowLoads()
    {
        await using var pair = await PoolManager.GetServerClient();

        await pair.Client.WaitAssertion(() =>
        {
            using var window = new ModSuitWindow();
            window.Measure(new Vector2(600, 600));
            window.Arrange(new UIBox2(0, 0, 600, 600));
        });

        await pair.CleanReturnAsync();
    }
}
