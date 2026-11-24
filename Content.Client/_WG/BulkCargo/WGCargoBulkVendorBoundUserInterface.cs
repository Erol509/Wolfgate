using Content.Shared._WG.BulkCargo;
using Content.Shared.VendingMachines;
using Robust.Client.UserInterface;

namespace Content.Client._WG.BulkCargo;

/// <summary>The trade vendor's bulk order window: cart, delivery zones and the live fill preview.</summary>
public sealed class WGCargoBulkVendorBoundUserInterface : BoundUserInterface
{
    private WGCargoBulkVendorWindow? _window;

    public WGCargoBulkVendorBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<WGCargoBulkVendorWindow>();
        _window.OnQuoteRequested += (items, zone, revision) =>
            SendMessage(new WGCargoBulkQuoteRequestMessage { Items = items, Zone = zone, Revision = revision });
        _window.OnOrderPressed += (quote, total) =>
            SendMessage(new WGCargoBulkPlaceOrderMessage { Quote = quote, ExpectedTotal = total });
        _window.OnCancelPressed += order =>
            SendMessage(new WGCargoBulkCancelMessage { Order = order });
        _window.OnDispensePressed += () => SendMessage(new WGCargoBulkDispenseMessage());
        _window.OnVendPressed += (id, type) => SendMessage(new VendingMachineEjectMessage(type, id));
        SendMessage(new WGCargoBulkRefreshMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is WGCargoBulkState bulk)
            _window?.UpdateState(bulk);
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);

        switch (message)
        {
            case WGCargoBulkBalanceMessage balance:
                _window?.UpdateBalance(balance);
                break;
            case WGCargoBulkOrdersMessage orders:
                _window?.UpdateOrders(orders);
                break;
            case WGCargoBulkQuoteMessage quote:
                _window?.UpdateQuote(quote);
                break;
            case WGCargoBulkOrderResultMessage result:
                _window?.UpdateOrderResult(result);
                break;
            case WGCargoBulkDispenseResultMessage dispense:
                _window?.UpdateDispenseResult(dispense);
                break;
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        _window?.Dispose();
        _window = null;
    }
}
