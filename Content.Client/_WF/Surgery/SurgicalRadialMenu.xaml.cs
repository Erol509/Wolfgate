using Content.Client.UserInterface.Controls;
using Robust.Client.UserInterface.XAML;

namespace Content.Client._WF.Surgery;

/// <summary>Radial wheel of the surgical operations currently available on a patient.</summary>
public sealed partial class SurgicalRadialMenu : RadialMenu
{
    public SurgicalRadialMenu()
    {
        RobustXamlLoader.Load(this);
    }
}
