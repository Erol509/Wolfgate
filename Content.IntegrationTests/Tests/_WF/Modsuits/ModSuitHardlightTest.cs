using System.Numerics;
using Content.Shared._WF.Modsuits;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Modsuits;

/// <summary>Checks the hardlight shader and per-theme hardlight data load on the client.</summary>
[TestFixture]
public sealed class ModSuitHardlightTest
{
    [Test]
    public async Task ThemesAndShaderLoad()
    {
        await using var pair = await PoolManager.GetServerClient();

        await pair.Client.WaitAssertion(() =>
        {
            var prototypes = pair.Client.ResolveDependency<IPrototypeManager>();
            var cache = pair.Client.ResolveDependency<IResourceCache>();
            Assert.That(prototypes.TryIndex<ShaderPrototype>("ModsuitHardlight", out var shaderProto), Is.True,
                "the MODsuit hardlight shader should be registered");

            using (var shader = shaderProto!.InstanceUnique())
            {
                shader.SetParameter("color", new Vector3(0.3f, 0.8f, 1f));
                shader.SetParameter("color2", new Vector3(0.1f, 0.3f, 0.5f));
                shader.SetParameter("alpha", 0.7f);
                shader.SetParameter("intensity", 1.1f);
                shader.SetParameter("gridScale", 32f);
                shader.SetParameter("t", 1f);
            }

            var hardlightThemes = 0;
            foreach (var theme in prototypes.EnumeratePrototypes<ModThemePrototype>())
            {
                if (!theme.Hardlight)
                    continue;

                hardlightThemes++;
                Assert.That(theme.HardlightColor.A, Is.GreaterThan(0f), $"{theme.ID} has a transparent hardlight colour");
                Assert.That(theme.HardlightColor2.A, Is.GreaterThan(0f), $"{theme.ID} has a transparent second hardlight colour");
                Assert.That(theme.HardlightGridScale, Is.GreaterThan(0f), $"{theme.ID} has no hardlight grid");
                Assert.That(cache.TryGetResource<TextureResource>(theme.HardlightMask, out _), Is.True,
                    $"{theme.ID} has no hardlight mask texture");
            }

            Assert.That(hardlightThemes, Is.GreaterThan(0));
        });

        await pair.CleanReturnAsync();
    }
}
