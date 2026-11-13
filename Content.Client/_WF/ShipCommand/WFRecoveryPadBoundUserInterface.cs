using Content.Shared._WF.ShipCommand;
using Robust.Client.UserInterface;

namespace Content.Client._WF.ShipCommand;

/// <summary>The recovery pad console's window. Its state comes as a message to this viewer alone.</summary>
public sealed class WFRecoveryPadBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [ViewVariables]
    private WFRecoveryPadWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<WFRecoveryPadWindow>();
        _window.OnRecall += (target, confirmed) => SendMessage(new WFRecoveryPadRecallMessage(target, confirmed));
        _window.OnAccess += access => SendMessage(new WFRecoveryPadAccessMessage(access));
        _window.OnRefresh += () => SendMessage(new WFRecoveryPadRefreshMessage());
        SendMessage(new WFRecoveryPadRefreshMessage());
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (message is WFRecoveryPadStateMessage state)
            _window?.UpdateState(state.State);
    }
}
