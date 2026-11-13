namespace Content.Server._WF.ShipCommand;

/// <summary>
/// A commanded ship's command console: its window (<see cref="WFShipConsoleSystem"/>) runs the ship it stands on.
/// Server only.
/// </summary>
[RegisterComponent]
public sealed partial class WFShipConsoleComponent : Component;
