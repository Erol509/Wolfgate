using Content.Shared._WF.Lattice;
using NUnit.Framework;

namespace Content.Tests._WF.Lattice;

/// <summary>Checks Kyphrus's implosion timeline: swell, fall to a point, then waves leaving in order.</summary>
[TestFixture]
public sealed class WFLatticeImplosionTest
{
    [Test]
    public void SwellsThenFallsToNothing()
    {
        Assert.That(WFLatticeImplosion.Scale(0f), Is.EqualTo(1f));
        Assert.That(WFLatticeImplosion.Scale(WFLatticeImplosion.Swell - 0.01f), Is.EqualTo(WFLatticeImplosion.SwellScale).Within(0.01f));
        Assert.That(WFLatticeImplosion.Scale(WFLatticeImplosion.Swell * 0.5f), Is.GreaterThan(1f));
        Assert.That(WFLatticeImplosion.Scale(WFLatticeImplosion.Collapse - 0.01f), Is.LessThan(0.01f));
        Assert.That(WFLatticeImplosion.Scale(WFLatticeImplosion.Burst), Is.EqualTo(0f));
    }

    [Test]
    public void WavesLeaveAtTheBurstInTurn()
    {
        Assert.That(WFLatticeImplosion.WaveRadius(WFLatticeImplosion.Burst - 1f, 0), Is.LessThanOrEqualTo(0f));
        Assert.That(WFLatticeImplosion.WaveRadius(WFLatticeImplosion.Burst + 1f, 0), Is.EqualTo(WFLatticeImplosion.WaveSpeed));
        Assert.That(WFLatticeImplosion.WaveRadius(WFLatticeImplosion.Burst + 1f, 1), Is.LessThanOrEqualTo(0f));
        Assert.That(WFLatticeImplosion.WaveRadius(WFLatticeImplosion.Burst + WFLatticeImplosion.WaveGap + 1f, 1),
            Is.EqualTo(WFLatticeImplosion.WaveSpeed));
    }
}
