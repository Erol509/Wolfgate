using Content.Client.UserInterface.Controls;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.XAML;
using Robust.Shared.Maths;

namespace Content.Client._WF.Surgery;

/// <summary>Radial wheel of the surgical operations currently available on a patient.</summary>
public sealed partial class SurgicalRadialMenu : RadialMenu
{
    public SurgicalRadialMenu()
    {
        RobustXamlLoader.Load(this);

        // The contextual centre button closes the wheel; make the meaning obvious.
        ContextualButton.AddChild(new Label
        {
            Text = "X",
            HorizontalAlignment = Control.HAlignment.Center,
            VerticalAlignment = Control.VAlignment.Center,
            FontColorOverride = Color.White,
            MouseFilter = Control.MouseFilterMode.Ignore,
        });
    }
}
