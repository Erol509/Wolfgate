using Content.Shared._WF.ShipShields;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._WF.ShipShields;

/// <summary>Uses the shared shield panel at an installed generator.</summary>
[UsedImplicitly]
public sealed class WFShipShieldGeneratorBoundUserInterface : BoundUserInterface
{
    private WFShipShieldGeneratorWindow? _window;

    /// <summary>Binds the panel to its generator.</summary>
    public WFShipShieldGeneratorBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<WFShipShieldGeneratorWindow>();
        _window.ShieldPanel.AllocationRequested += (direction, concentration, arc) =>
            SendMessage(new WFShipShieldSetShuntMessage(direction, concentration, arc));
        _window.ShieldPanel.OnSetEnabled += enabled => SendMessage(new WFShipShieldSetEnabledMessage(enabled));
        _window.StrengthRequested += strength => SendMessage(new WFShipShieldSetStrengthMessage(strength));
        if (State is WFShipShieldGeneratorUiState state)
            Show(state);
        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is WFShipShieldGeneratorUiState shield)
            Show(shield);
    }

    private void Show(WFShipShieldGeneratorUiState state)
    {
        if (_window == null)
            return;

        _window.ShieldPanel.UpdateState(state.ShieldShunt, 0f);
        if (EntMan.HasComponent<WFShipShieldStrengthControlComponent>(Owner) && state.ShieldShunt.Stats is { } stats)
            _window.ShowStrength(stats.DamageLimit);
    }
}
