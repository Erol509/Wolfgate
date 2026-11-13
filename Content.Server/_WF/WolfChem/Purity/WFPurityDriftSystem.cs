using Content.Server.GameTicking.Events;
using Content.Shared._WF.WolfChem.Purity;
using Content.Shared.Chemistry.Reaction;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WF.WolfChem.Purity;

/// <summary>
/// Moves each graded reaction's optimal temperature by up to its <see cref="WFReactionPurityPrototype.Drift"/> at
/// round start, so last round's numbers are only close.
/// </summary>
public sealed class WFPurityDriftSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ChemicalReactionSystem _reactions = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundStartingEvent>(_ => Roll());
    }

    /// <summary>Rolls a new shift for every reaction that drifts.</summary>
    public void Roll()
    {
        _reactions.WFOptimumShift.Clear();
        foreach (var purity in _proto.EnumeratePrototypes<WFReactionPurityPrototype>())
        {
            if (purity.OptimalTemp != null && purity.Drift > 0f)
                _reactions.WFOptimumShift[purity.ID] = _random.NextFloat(-purity.Drift, purity.Drift);
        }
    }
}
