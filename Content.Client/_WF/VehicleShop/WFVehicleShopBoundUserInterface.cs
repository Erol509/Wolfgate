using Content.Shared._WF.VehicleShop;
using Robust.Client.UserInterface;

namespace Content.Client._WF.VehicleShop;

/// <summary>
/// Opens a vehicle purchase terminal's window and sends its buy, sell and repair presses. Its state comes as a message
/// to this customer alone, since prices differ by customer.
/// </summary>
public sealed class WFVehicleShopBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private WFVehicleShopWindow? _window;

    public WFVehicleShopBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<WFVehicleShopWindow>();
        _window.Title = EntMan.GetComponent<MetaDataComponent>(Owner).EntityName;
        _window.OnBuy += index => SendMessage(new WFVehicleShopBuyMessage(index));
        _window.OnSell += () => SendMessage(new WFVehicleShopSellMessage());
        _window.OnRepair += () => SendMessage(new WFVehicleShopRepairMessage());
        SendMessage(new WFVehicleShopRefreshMessage());
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);

        if (message is WFVehicleShopStateMessage shop)
            _window?.UpdateState(shop.State);
    }
}
