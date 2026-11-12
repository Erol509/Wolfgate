using Content.Client.UserInterface.Controls;
using Robust.Client.Graphics;

namespace Content.Client._WF.Surgery;

/// <summary>Operation slot of the surgical wheel, drawn with /tg/station's radial slice sprites.</summary>
public sealed class SurgicalRadialButton : RadialMenuTextureButton
{
    private readonly Texture _slice;
    private readonly Texture _focus;

    public SurgicalRadialButton(Texture slice, Texture focus)
    {
        _slice = slice;
        _focus = focus;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var texture = DrawMode is DrawModeEnum.Hover or DrawModeEnum.Pressed ? _focus : _slice;
        handle.DrawTextureRect(texture, PixelSizeBox);
    }
}
