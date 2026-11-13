using Content.Shared._WF.Drydock;
using Robust.Client.UserInterface;

namespace Content.Client._WF.Drydock;

/// <summary>
/// Client UI for the drydock berth console: its shared state, and this viewer's own prices as a message.
/// </summary>
public sealed class WFDrydockConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private WFDrydockConsoleWindow? _window;

    public WFDrydockConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<WFDrydockConsoleWindow>();
        _window.OnRepairPressed += OnRepairPressed;
        _window.OnBreakPressed += () => SendMessage(new WFDrydockBreakMessage());
        _window.OnCleanDriftPressed += () => SendMessage(new WFDrydockCleanDriftMessage());
        _window.OnUpgradePressed += OnUpgradePressed;
        _window.OnCancelBookingPressed += OnCancelBookingPressed;
        _window.OnBerthLightsPressed += OnBerthLightsPressed;
        SendMessage(new WFDrydockQuoteRequestMessage());
    }

    private void OnBerthLightsPressed()
    {
        SendMessage(new WFDrydockToggleBerthLightsMessage());
    }

    private void OnRepairPressed()
    {
        SendMessage(new WFDrydockRepairMessage());
    }

    private void OnUpgradePressed(string upgrade)
    {
        SendMessage(new WFDrydockUpgradeMessage(upgrade));
    }

    private void OnCancelBookingPressed()
    {
        SendMessage(new WFDrydockCancelBookingMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not WFDrydockConsoleState castState)
            return;

        _window?.UpdateState(castState);
    }

    /// <summary>Takes this viewer's own prices, which the console sends apart from its shared state.</summary>
    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);

        if (message is WFDrydockQuoteMessage quote)
            _window?.UpdateQuote(quote.Quote);
    }
}
