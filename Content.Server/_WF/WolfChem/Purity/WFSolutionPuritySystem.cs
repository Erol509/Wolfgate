using Content.Server.Chemistry.Containers.EntitySystems;

namespace Content.Server._WF.WolfChem.Purity;

/// <summary>
/// Applies <see cref="WFSolutionPurityComponent"/> grades once the entity's solutions exist.
/// </summary>
public sealed class WFSolutionPuritySystem : EntitySystem
{
    [Dependency] private SolutionContainerSystem _solutions = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFSolutionPurityComponent, MapInitEvent>(OnMapInit, after: new[] { typeof(SolutionContainerSystem) });
    }

    private void OnMapInit(Entity<WFSolutionPurityComponent> ent, ref MapInitEvent args)
    {
        if (!_solutions.TryGetSolution(ent.Owner, ent.Comp.Solution, out var soln, out var solution))
            return;

        foreach (var (reagent, grade) in ent.Comp.Reagents)
        {
            if (solution.ContainsPrototype(reagent))
                solution.SetPurity(reagent, grade);
        }

        Dirty(soln.Value);
    }
}
