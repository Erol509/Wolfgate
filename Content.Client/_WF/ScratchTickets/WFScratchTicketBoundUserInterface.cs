using Content.Shared._WF.ScratchTickets;
using Robust.Client.UserInterface;

namespace Content.Client._WF.ScratchTickets;

/// <summary>Opens a scratch ticket and tells the server once its foil is off.</summary>
public sealed class WFScratchTicketBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private WFScratchTicketWindow? _window;

    public WFScratchTicketBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        if (!EntMan.TryGetComponent(Owner, out WFScratchTicketComponent? ticket))
            return;

        var foil = EntMan.EnsureComponent<WFScratchFoilComponent>(Owner);
        if (foil.Alpha.Length != WFScratchFoil.Resolution * WFScratchFoil.Resolution)
        {
            foil.Alpha = new byte[WFScratchFoil.Resolution * WFScratchFoil.Resolution];
            Array.Fill(foil.Alpha, byte.MaxValue);
        }

        _window = this.CreateWindow<WFScratchTicketWindow>();
        _window.SetTicket(Owner, ticket, foil.Alpha);
        _window.OnRevealed += () => SendMessage(new WFScratchTicketRevealedMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is WFScratchTicketState ticket)
            _window?.UpdateState(ticket);
    }
}
