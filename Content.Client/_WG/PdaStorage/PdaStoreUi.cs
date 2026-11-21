using Content.Client.UserInterface.Fragments;
using Content.Shared._WG.PdaStorage;
using Content.Shared.CartridgeLoader;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;

namespace Content.Client._WG.PdaStorage;

public sealed partial class PdaStoreUi : UIFragment
{
    private PdaStoreUiFragment? _fragment;

    public override Control GetUIFragmentRoot()
    {
        return _fragment!;
    }

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new PdaStoreUiFragment();
        _fragment.OnAction += (action, appId, program) =>
        {
            var message = new PdaStoreMessage(action, appId, program);
            userInterface.SendMessage(new CartridgeUiMessage(message));
        };
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is PdaStoreUiState storeState)
            _fragment?.UpdateState(storeState);
    }
}
