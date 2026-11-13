using Content.Shared._WF.ShipCommand;
using Robust.Client.UserInterface;

namespace Content.Client._WF.ShipCommand;

/// <summary>The ship command console's window. Its state comes as a message to this viewer alone.</summary>
public sealed class WFShipConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [ViewVariables]
    private WFShipConsoleWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<WFShipConsoleWindow>();
        _window.OnSend += SendMessage;
        SendMessage(new WFShipConsoleRefreshMessage());
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (message is WFShipConsoleStateMessage state)
            _window?.UpdateState(state.State);
    }
}
