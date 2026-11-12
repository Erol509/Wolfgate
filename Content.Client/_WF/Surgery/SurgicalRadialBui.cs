using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._WF.Surgery;
using JetBrains.Annotations;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._WF.Surgery;

/// <summary>Client half of the surgical radial. The server sends the slices it has validated.</summary>
[UsedImplicitly]
public partial class SurgicalRadialBui : BoundUserInterface
{
    private static readonly SpriteSpecifier DefaultIcon =
        new SpriteSpecifier.Rsi(new ResPath("/Textures/_Shitmed/Objects/Specific/Medical/Surgery/scalpel.rsi"), "scalpel");

    [Dependency] private IClyde _display = default!;
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IInputManager _input = default!;
    [Dependency] private IPlayerManager _player = default!;

    private readonly SpriteSystem _sprites;
    private readonly TransformSystem _transform;

    [ViewVariables]
    private SurgicalRadialMenu? _menu;

    public SurgicalRadialBui(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        _sprites = EntMan.System<SpriteSystem>();
        _transform = EntMan.System<TransformSystem>();
    }

    protected override void Open()
    {
        base.Open();

        _menu = this.CreateWindow<SurgicalRadialMenu>();
        Build();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        Build();
    }

    private void Build()
    {
        if (_menu == null || State is not SurgicalRadialState state)
            return;

        var container = _menu.FindControl<RadialContainer>("Main");
        container.DisposeAllChildren();

        foreach (var option in state.Options)
        {
            var icon = _sprites.Frame0(option.Icon ?? DefaultIcon);
            var texture = new TextureRect
            {
                VerticalAlignment = Control.VAlignment.Center,
                HorizontalAlignment = Control.HAlignment.Center,
                Texture = icon,
                TextureScale = new Vector2(2f, 2f),
            };

            var button = new RadialMenuTextureButtonWithSector
            {
                SetSize = new Vector2(64, 64),
                ToolTip = option.Tool.Length > 0
                    ? $"{option.Name}\n{option.Description}\n{Loc.GetString("surgery-radial-tool", ("tool", option.Tool))}"
                    : $"{option.Name}\n{option.Description}",
            };

            var index = option.Index;
            button.OnButtonDown += _ => SendMessage(new SurgicalRadialChooseMessage(index));
            button.AddChild(texture);
            container.AddChild(button);
        }

        if (!_menu.IsOpen)
        {
            var viewport = _display.ScreenSize;
            var position = _input.MouseScreenPosition.Position / viewport;

            if (EntMan.TryGetEntity(state.Target, out var target) && target is { } targetUid)
                position = _eye.WorldToScreen(_transform.GetMapCoordinates(targetUid).Position) / viewport;
            else if (_player.LocalEntity is { } local)
                position = _eye.WorldToScreen(_transform.GetMapCoordinates(local).Position) / viewport;

            _menu.OpenCenteredAt(position);
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _menu?.Dispose();
    }
}
