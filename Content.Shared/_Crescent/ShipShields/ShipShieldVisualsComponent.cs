// WOLFGATE: restore the licence header removed by Kill Spdx
// SPDX-FileCopyrightText: 2025 ark1368
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.GameStates;

namespace Content.Shared._Crescent.ShipShields;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ShipShieldVisualsComponent : Component
{
    /// <summary>
    /// The color of this shield.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Color ShieldColor = Color.White;

    /// <summary>
    /// The extra padding of this shield.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Padding = 50f;
}
