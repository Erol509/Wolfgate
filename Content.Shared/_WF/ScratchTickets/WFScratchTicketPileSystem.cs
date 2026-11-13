using Content.Shared.Stacks;
using Content.Shared.UserInterface;

namespace Content.Shared._WF.ScratchTickets;

/// <summary>Keeps a pile of scratch tickets from opening as a card; the server pulls one ticket out instead.</summary>
public sealed class WFScratchTicketPileSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFScratchTicketComponent, ActivatableUIOpenAttemptEvent>(OnOpenAttempt);
    }

    private void OnOpenAttempt(Entity<WFScratchTicketComponent> ent, ref ActivatableUIOpenAttemptEvent args)
    {
        if (TryComp<StackComponent>(ent, out var stack) && stack.Count > 1)
            args.Cancel();
    }
}
