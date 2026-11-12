using System;
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
using Robust.Shared.GameObjects;
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
    private readonly Texture _slice;
    private readonly Texture _sliceFocus;
    private readonly Texture _next;

    private SurgicalRadialState? _shownState;
    private int _page;

    // /tg/station caps a page at eight slots and reserves one of them for the next-page button.
    private const int MaxSlots = 8;

    [ViewVariables]
    private SurgicalRadialMenu? _menu;

    private OrganSelectionWindow? _organWindow;

    public SurgicalRadialBui(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        _sprites = EntMan.System<SpriteSystem>();
        _transform = EntMan.System<TransformSystem>();
        _slice = Frame("radial_slice");
        _sliceFocus = Frame("radial_slice_focus");
        _next = Frame("radial_next");
    }

    private Texture Frame(string state) =>
        _sprites.Frame0(new SpriteSpecifier.Rsi(new ResPath("/Textures/_WF/Surgery/RadialUI.rsi"), state));

    protected override void Open()
    {
        base.Open();

        _menu = this.CreateWindow<SurgicalRadialMenu>();

        if (State is SurgicalOrganSelectState organState)
            ShowOrganPicker(organState);
        else
            Build();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is SurgicalOrganSelectState organState)
        {
            ShowOrganPicker(organState);
            return;
        }

        Build();
    }

    /// <summary>Switches from the radial to the organ list the generic organ-manipulation operation opened.</summary>
    private void ShowOrganPicker(SurgicalOrganSelectState state)
    {
        // Closing a CreateWindow window closes the whole BUI, and the UI system calls UpdateState from inside
        // its queued open/close loop, so that would mutate the queue while it is enumerated. Hide the wheel
        // instead; Build() shows it again if the state ever returns to it.
        if (_menu is { Visible: true })
            _menu.Visible = false;

        if (_organWindow == null)
        {
            _organWindow = new OrganSelectionWindow();
            _organWindow.OnChosen += OnOrganChosen;
            _organWindow.OnCancelled += OnOrganCancelled;
        }

        _organWindow.SetOptions(state);
        if (!_organWindow.IsOpen)
            _organWindow.OpenCentered();
    }

    private void OnOrganChosen(NetEntity? organ, bool inserting)
    {
        SendMessage(new SurgicalOrganChooseMessage(organ, inserting));
    }

    private void OnOrganCancelled()
    {
        SendMessage(new SurgicalOrganCancelMessage());
    }

    private void Build()
    {
        if (_menu == null || State is not SurgicalRadialState state)
            return;

        if (!_menu.Visible)
            _menu.Visible = true;

        if (!ReferenceEquals(state, _shownState))
        {
            _shownState = state;
            _page = 0;
        }

        var options = state.Options;
        var perPage = options.Count > MaxSlots ? MaxSlots - 1 : MaxSlots;
        var pages = Math.Max(1, (options.Count + perPage - 1) / perPage);
        _page = Math.Clamp(_page, 0, pages - 1);

        var container = _menu.FindControl<RadialContainer>("Main");
        container.DisposeAllChildren();

        var first = _page * perPage;
        var last = Math.Min(options.Count, first + perPage);
        for (var i = first; i < last; i++)
        {
            var option = options[i];
            var button = new SurgicalRadialButton(_slice, _sliceFocus)
            {
                SetSize = new Vector2(64, 64),
                ToolTip = option.Tool.Length > 0
                    ? $"{option.Name}\n{option.Description}\n{Loc.GetString("surgery-radial-tool", ("tool", option.Tool))}"
                    : $"{option.Name}\n{option.Description}",
            };

            var index = option.Index;
            button.OnButtonDown += _ => SendMessage(new SurgicalRadialChooseMessage(index));
            button.AddChild(Icon(option.Icon));
            container.AddChild(button);
        }

        if (pages > 1)
        {
            var next = new SurgicalRadialButton(_slice, _sliceFocus)
            {
                SetSize = new Vector2(64, 64),
                ToolTip = Loc.GetString("surgery-radial-next-page", ("page", _page + 1), ("pages", pages)),
            };

            next.OnButtonDown += _ =>
            {
                _page = (_page + 1) % pages;
                Build();
            };
            next.AddChild(Icon(null, _next));
            container.AddChild(next);
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

    private TextureRect Icon(SpriteSpecifier? specifier, Texture? texture = null)
    {
        return new TextureRect
        {
            VerticalAlignment = Control.VAlignment.Center,
            HorizontalAlignment = Control.HAlignment.Center,
            Texture = texture ?? _sprites.Frame0(specifier ?? DefaultIcon),
            TextureScale = new Vector2(2f, 2f),
        };
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        _menu?.Dispose();
        _organWindow?.Dispose();
    }
}
