using System;
using System.Numerics;
using Content.Client.Resources;
using Content.Shared._Shitmed.Targeting;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client._WF.Surgery;

/// <summary>
/// The operating computer's body zone selector: a 32x32 body scaled three times, with the selected and
/// hovered zone overlaid, matching /tg/station's BodyZoneSelector.
/// </summary>
public sealed class BodyZoneSelectorControl : Control
{
    private const int Scale = 3;
    private const int Size = 32 * Scale;

    private static readonly ResPath BasePath = new("/Textures/_WF/Surgery/BodyZones/base.png");

    private readonly TextureRect _base;
    private readonly TextureRect _selected;
    private readonly TextureRect _hover;

    private TargetBodyPart _selectedZone = TargetBodyPart.Torso;
    private TargetBodyPart? _hoverZone;

    public event Action<TargetBodyPart>? ZoneChosen;

    public BodyZoneSelectorControl()
    {
        MinSize = new Vector2(Size, Size);
        SetSize = new Vector2(Size, Size);
        MouseFilter = Control.MouseFilterMode.Stop;

        _base = MakeTexture(BasePath);
        _selected = MakeTexture(null);
        _hover = MakeTexture(null);

        AddChild(_base);
        AddChild(_selected);
        AddChild(_hover);

        SetSelected(TargetBodyPart.Torso);
    }

    /// <summary>Highlights the zone the server is currently operating on.</summary>
    public void SetSelected(TargetBodyPart zone)
    {
        _selectedZone = zone;
        _selected.Texture = LoadZone(zone);
        _selected.Visible = _selected.Texture != null;
    }

    private static TextureRect MakeTexture(ResPath? path)
    {
        return new TextureRect
        {
            Texture = path is { } p ? IoCManager.Resolve<IResourceCache>().GetTexture(p) : null,
            SetSize = new Vector2(Size, Size),
            Stretch = TextureRect.StretchMode.Scale,
            MouseFilter = Control.MouseFilterMode.Ignore,
            Visible = path != null,
        };
    }

    private static Texture? LoadZone(TargetBodyPart zone)
    {
        var file = zone switch
        {
            TargetBodyPart.Head => "head",
            TargetBodyPart.Torso => "chest",
            TargetBodyPart.Groin => "groin",
            TargetBodyPart.LeftArm or TargetBodyPart.LeftHand => "l_arm",
            TargetBodyPart.RightArm or TargetBodyPart.RightHand => "r_arm",
            TargetBodyPart.LeftLeg or TargetBodyPart.LeftFoot => "l_leg",
            TargetBodyPart.RightLeg or TargetBodyPart.RightFoot => "r_leg",
            _ => null,
        };

        return file == null
            ? null
            : IoCManager.Resolve<IResourceCache>().GetTexture(new ResPath($"/Textures/_WF/Surgery/BodyZones/{file}.png"));
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);

        var zone = ZoneAt(args.RelativePosition);
        if (zone == _hoverZone)
            return;

        _hoverZone = zone;
        _hover.Texture = zone is { } hovered ? LoadZone(hovered) : null;
        _hover.Visible = _hover.Texture != null;
        _hover.ModulateSelfOverride = Color.White.WithAlpha(0.5f);
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        if (ZoneAt(args.RelativePosition) is not { } zone)
            return;

        SetSelected(zone);
        ZoneChosen?.Invoke(zone);
        args.Handle();
    }

    /// <summary>Port of /tg/station's <c>bodyZonePixelToZone</c>, without the precise eyes/mouth split.</summary>
    private static TargetBodyPart? ZoneAt(Vector2 position)
    {
        var x = position.X / Scale;
        var y = (Size - position.Y) / Scale;

        if (y < 1f)
            return null;

        if (y < 4f)
        {
            if (x > 10f && x < 15f)
                return TargetBodyPart.RightFoot;
            if (x > 17f && x < 22f)
                return TargetBodyPart.LeftFoot;
        }

        if (y < 10f)
        {
            if (x > 10f && x < 15f)
                return TargetBodyPart.RightLeg;
            if (x > 17f && x < 22f)
                return TargetBodyPart.LeftLeg;
        }
        else if (y < 13f)
        {
            if (x > 8f && x < 11f)
                return TargetBodyPart.RightHand;
            if (x > 12f && x < 20f)
                return TargetBodyPart.Groin;
            if (x > 21f && x < 24f)
                return TargetBodyPart.LeftHand;
        }
        else if (y < 22f)
        {
            if (x > 8f && x < 11f)
                return TargetBodyPart.RightArm;
            if (x > 12f && x < 20f)
                return TargetBodyPart.Torso;
            if (x > 21f && x < 24f)
                return TargetBodyPart.LeftArm;
        }
        else if (y < 30f && x > 12f && x < 20f)
        {
            return TargetBodyPart.Head;
        }

        return null;
    }
}
