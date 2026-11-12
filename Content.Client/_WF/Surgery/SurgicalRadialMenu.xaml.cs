using System.Numerics;
using Content.Client.UserInterface.Controls;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.XAML;

namespace Content.Client._WF.Surgery;

/// <summary>Radial wheel of the surgical operations currently available on a patient.</summary>
public sealed partial class SurgicalRadialMenu : RadialMenu
{
    private const string RsiPath = "/Textures/_WF/Surgery/RadialUI.rsi";

    public SurgicalRadialMenu()
    {
        RobustXamlLoader.Load(this);

        // The contextual centre button closes the wheel, drawn like the operation slots.
        var rsi = IoCManager.Resolve<IResourceCache>().GetResource<RSIResource>(RsiPath).RSI;
        var normal = rsi["radial_center"].Frame0;
        var focus = rsi["radial_center_focus"].Frame0;
        var icon = new TextureRect
        {
            Texture = normal,
            SetSize = new Vector2(64, 64),
            MouseFilter = MouseFilterMode.Ignore,
        };

        ContextualButton.AddChild(icon);
        ContextualButton.ToolTip = Loc.GetString("surgery-radial-close");
        ContextualButton.OnMouseEntered += _ => icon.Texture = focus;
        ContextualButton.OnMouseExited += _ => icon.Texture = normal;
    }
}
