using System.Linq;
using System.Numerics;
using System.Text;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Surgery;
using JetBrains.Annotations;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client._WF.Surgery;

/// <summary>Operating computer window, laid out after /tg/station's OperatingComputer TGUI.</summary>
[UsedImplicitly]
public sealed partial class OperatingComputerBui : BoundUserInterface
{
    private static readonly Color BaseColor = Color.FromHex("#005C2E");
    private static readonly Color PanelColor = Color.FromHex("#0A6B38");
    private static readonly Color BorderColor = Color.FromHex("#C8C8C8");
    private static readonly Color TextColor = Color.FromHex("#E8E8E8");
    private static readonly Color MutedColor = Color.FromHex("#B8D8C4");
    private static readonly Color SelectedColor = Color.FromHex("#9E0808");
    private static readonly Color CautionColor = Color.FromHex("#B85C00");
    private static readonly Color DangerColor = Color.FromHex("#9E9E00");
    private static readonly Color GoodColor = Color.FromHex("#3CB371");
    private static readonly Color AverageColor = Color.FromHex("#DAA520");
    private static readonly Color BadColor = Color.FromHex("#B22222");

    private static readonly (TargetBodyPart Zone, string Label)[] ZoneButtons =
    {
        (TargetBodyPart.Head, "Head"),
        (TargetBodyPart.Torso, "Torso"),
        (TargetBodyPart.LeftArm, "L. Arm"),
        (TargetBodyPart.RightArm, "R. Arm"),
        (TargetBodyPart.LeftHand, "L. Hand"),
        (TargetBodyPart.RightHand, "R. Hand"),
        (TargetBodyPart.LeftLeg, "L. Leg"),
        (TargetBodyPart.RightLeg, "R. Leg"),
        (TargetBodyPart.LeftFoot, "L. Foot"),
        (TargetBodyPart.RightFoot, "R. Foot"),
    };

    private OperatingComputerWindow? _window;
    private OperatingComputerState? _state;
    private OperatingComputerCatalogState? _catalog;

    private readonly HashSet<string> _pins = new();
    private readonly List<Button> _tabButtons = new();
    private int _tab;
    private string _search = string.Empty;
    private string _filterTool = string.Empty;
    private int _sortMode;
    private bool _hideMechanic;

    public OperatingComputerBui(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        IoCManager.InjectDependencies(this);
    }

    protected override void Open()
    {
        base.Open();

        _window = new OperatingComputerWindow();
        _window.OnClose += Close;
        _window.OpenCentered();

        Rebuild();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        switch (state)
        {
            case OperatingComputerState dynamicState:
                _state = dynamicState;
                break;
            case OperatingComputerCatalogState catalogState:
                _catalog = catalogState;
                break;
            default:
                return;
        }

        Rebuild();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _window?.Dispose();
    }

    private void Rebuild()
    {
        if (_window == null || _state == null)
            return;

        _window.Title = Loc.GetString("operating-computer-title");
        _window.SetSize = new Vector2(_tab == 0 ? 350 : 430, 610);

        var root = _window.Backdrop;
        root.PanelOverride = new StyleBoxFlat
        {
            BackgroundColor = BaseColor,
            BorderColor = BorderColor,
            BorderThickness = new Thickness(2),
            ContentMarginLeftOverride = 0,
            ContentMarginRightOverride = 0,
            ContentMarginTopOverride = 0,
            ContentMarginBottomOverride = 0,
        };

        _window.Separator.PanelOverride = new StyleBoxFlat
        {
            BackgroundColor = BorderColor,
            ContentMarginLeftOverride = 0,
            ContentMarginRightOverride = 0,
            ContentMarginTopOverride = 0,
            ContentMarginBottomOverride = 0,
        };

        _window.Footer.FontColorOverride = MutedColor;
        _window.Footer.Text = Loc.GetString("operating-computer-footer");

        BuildTabs();

        _window.PatientRoot.Visible = _tab == 0;
        _window.CatalogBar.Visible = _tab == 1;
        _window.CatalogScroll.Visible = _tab == 1;

        if (_tab == 0)
            BuildPatient();
        else if (_tab == 1)
            BuildCatalog();
        else
            BuildExperiments();
    }

    private void BuildTabs()
    {
        var bar = _window!.TabBar;
        bar.DisposeAllChildren();
        _tabButtons.Clear();

        var labels = new[]
        {
            Loc.GetString("operating-computer-tab-patient"),
            Loc.GetString("operating-computer-tab-catalog"),
            Loc.GetString("operating-computer-tab-experiments"),
        };

        for (var i = 0; i < labels.Length; i++)
        {
            var index = i;
            var button = new Button
            {
                Text = labels[i],
                HorizontalExpand = true,
                ModulateSelfOverride = _tab == i ? SelectedColor : PanelColor,
                Label = { FontColorOverride = TextColor },
            };
            button.OnPressed += _ =>
            {
                _tab = index;
                Rebuild();
            };

            _tabButtons.Add(button);
            bar.AddChild(button);
        }
    }

    private void BuildPatient()
    {
        var root = _window!.PatientRoot;
        root.DisposeAllChildren();

        if (!_state!.HasTable)
        {
            root.AddChild(Notice(Loc.GetString("operating-computer-no-table"), CautionColor));
            return;
        }

        if (!_state.HasPatient)
        {
            root.AddChild(Notice(Loc.GetString("operating-computer-no-patient"), BadColor));
            return;
        }

        root.AddChild(LabeledRow(Loc.GetString("operating-computer-state"), _state.PatientState, StateColor(_state.PatientStateColor)));
        root.AddChild(LabeledRow(Loc.GetString("operating-computer-blood-type"), _state.BloodType));
        root.AddChild(LabeledProgress(Loc.GetString("operating-computer-health"), _state.Health, _state.MinHealth,
            _state.MaxHealth, GoodColor, $"{MathF.Round(_state.Health / MathF.Max(1f, _state.MaxHealth) * 100f)}%"));
        var bloodRatio = _state.StandardBloodLevel > 0f ? _state.BloodLevel / _state.StandardBloodLevel : 0f;
        root.AddChild(LabeledProgress(Loc.GetString("operating-computer-blood-level"), _state.BloodLevel, 0f,
            _state.StandardBloodLevel, bloodRatio >= 0.7f ? GoodColor : bloodRatio >= 0.45f ? AverageColor : BadColor,
            $"{MathF.Round(bloodRatio * 100f)}%"));
        root.AddChild(LabeledProgress(Loc.GetString("operating-computer-brute"), _state.Brute, 0f, _state.MaxHealth, BadColor,
            MathF.Round(_state.Brute).ToString()));
        root.AddChild(LabeledProgress(Loc.GetString("operating-computer-burn"), _state.Burn, 0f, _state.MaxHealth, BadColor,
            MathF.Round(_state.Burn).ToString()));
        root.AddChild(LabeledProgress(Loc.GetString("operating-computer-toxin"), _state.Toxin, 0f, _state.MaxHealth, BadColor,
            MathF.Round(_state.Toxin).ToString()));
        root.AddChild(LabeledProgress(Loc.GetString("operating-computer-respiratory"), _state.Oxy, 0f, _state.MaxHealth, BadColor,
            MathF.Round(_state.Oxy).ToString()));

        root.AddChild(new Control { MinSize = new Vector2(0, 6) });

        var stateRow = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true };
        stateRow.AddChild(BuildZoneSelector());

        var stateList = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(8, 0, 0, 0) };
        foreach (var line in _state.SurgeryState)
            stateList.AddChild(Text($"- {line}", MutedColor));
        stateRow.AddChild(stateList);
        root.AddChild(stateRow);

        var operationsSection = new PanelContainer
        {
            VerticalExpand = true,
            Margin = new Thickness(0, 6, 0, 0),
            PanelOverride = SectionBox(),
        };

        var operations = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            VerticalExpand = true,
            Margin = new Thickness(4),
        };

        operations.AddChild(Text(Loc.GetString("operating-computer-next-operations"), TextColor));

        var scroll = new ScrollContainer { VerticalExpand = true, HorizontalExpand = true };
        var list = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
        scroll.AddChild(list);

        var next = GetCatalogOperations()
            .Where(operation => _state.AvailableOperations.Contains(operation.Id))
            .OrderByDescending(operation => _pins.Contains(operation.Name))
            .ThenByDescending(operation => operation.Priority)
            .ThenBy(operation => operation.Name)
            .Where(operation => _filterTool.Length == 0 || operation.Tool.Contains(_filterTool, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (next.Count == 0)
            list.AddChild(Notice(Loc.GetString("operating-computer-no-operations"), SelectedColor));

        foreach (var operation in next)
            list.AddChild(OperationButton(operation));

        operations.AddChild(scroll);
        operationsSection.AddChild(operations);
        root.AddChild(operationsSection);
    }

    private Control BuildZoneSelector()
    {
        var grid = new GridContainer { Columns = 3, HSeparationOverride = 2, VSeparationOverride = 2 };

        foreach (var (zone, label) in ZoneButtons)
        {
            var selected = (int) zone == _state!.Zone;
            var button = new Button
            {
                Text = label,
                MinSize = new Vector2(66, 26),
                ModulateSelfOverride = selected ? SelectedColor : PanelColor,
                Label = { FontColorOverride = TextColor },
            };

            button.OnPressed += _ => SendMessage(new OperatingComputerChangeZoneMessage((int) zone));
            grid.AddChild(button);
        }

        return grid;
    }

    private void BuildCatalog()
    {
        BuildCatalogBar();
        BuildCatalogList();
    }

    private string SortLabel() => _sortMode switch
    {
        1 => Loc.GetString("operating-computer-sort-name"),
        2 => Loc.GetString("operating-computer-sort-tool"),
        _ => Loc.GetString("operating-computer-sort-default"),
    };

    private void BuildCatalogBar()
    {
        var bar = _window!.CatalogBar;
        if (bar.ChildCount > 0)
            return;

        var search = new LineEdit
        {
            PlaceHolder = Loc.GetString("operating-computer-search"),
            Text = _search,
            HorizontalExpand = true,
            MinSize = new Vector2(180, 0),
        };
        search.OnTextChanged += args =>
        {
            _search = args.Text;
            BuildCatalogList();
        };
        bar.AddChild(search);

        var hide = new Button
        {
            Text = Loc.GetString("operating-computer-hide-mechanic"),
            ToggleMode = true,
            Pressed = _hideMechanic,
            ModulateSelfOverride = _hideMechanic ? SelectedColor : PanelColor,
            Label = { FontColorOverride = TextColor },
        };
        hide.OnToggled += args =>
        {
            _hideMechanic = args.Pressed;
            BuildCatalogList();
        };
        bar.AddChild(hide);

        var sort = new Button
        {
            Text = SortLabel(),
            MinSize = new Vector2(82, 0),
            ModulateSelfOverride = PanelColor,
            Label = { FontColorOverride = TextColor },
        };
        sort.OnPressed += _ =>
        {
            _sortMode = (_sortMode + 1) % 3;
            sort.Text = SortLabel();
            BuildCatalogList();
        };
        bar.AddChild(sort);
    }

    private void BuildCatalogList()
    {
        var root = _window!.CatalogRoot;
        root.DisposeAllChildren();

        var operations = GetCatalogOperations()
            .Where(operation => !_hideMechanic || !operation.Mechanic)
            .Where(operation => _search.Length == 0 ||
                                operation.Name.Contains(_search, StringComparison.OrdinalIgnoreCase) ||
                                operation.Tool.Contains(_search, StringComparison.OrdinalIgnoreCase));

        operations = _sortMode switch
        {
            1 => operations.OrderBy(operation => operation.Name),
            2 => operations.OrderBy(operation => operation.Tool),
            _ => operations.OrderByDescending(operation => _pins.Contains(operation.Name))
                .ThenBy(operation => operation.Name),
        };

        foreach (var operation in operations)
        {
            var entry = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                Margin = new Thickness(0, 0, 0, 8),
                HorizontalExpand = true,
            };

            var header = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
            var pin = new Button
            {
                Text = "Pin",
                MinSize = new Vector2(38, 0),
                ModulateSelfOverride = _pins.Contains(operation.Name) ? SelectedColor : PanelColor,
                Label = { FontColorOverride = TextColor },
            };
            pin.OnPressed += _ =>
            {
                if (!_pins.Remove(operation.Name))
                    _pins.Add(operation.Name);
                Rebuild();
            };
            header.AddChild(pin);

            header.AddChild(Text($" {operation.Name}", TextColor));

            if (operation.Locked)
                header.AddChild(Text($"  ({Loc.GetString("operating-computer-locked")})", DangerColor));

            var spacer = new Control { HorizontalExpand = true };
            header.AddChild(spacer);
            header.AddChild(Text(operation.Tool, MutedColor));
            entry.AddChild(header);

            var body = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                Margin = new Thickness(42, 2, 0, 0),
            };

            var border = new PanelContainer
            {
                PanelOverride = new StyleBoxFlat
                {
                    BackgroundColor = Color.Transparent,
                    BorderColor = MutedColor,
                    BorderThickness = new Thickness(0, 0, 0, 0),
                    ContentMarginLeftOverride = 4,
                    ContentMarginTopOverride = 2,
                },
            };

            var inner = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
            inner.AddChild(Text(Wrap(operation.Description, 58), MutedColor));

            var requirementsBody = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
            foreach (var requirement in operation.Requirements)
                requirementsBody.AddChild(Text($"- {requirement}", MutedColor));

            inner.AddChild(new Collapsible(Loc.GetString("operating-computer-requirements"), new CollapsibleBody())
            {
                BodyVisible = _pins.Contains(operation.Name),
            });

            // The Collapsible ctor needs a body child; fill the one it created.
            if (inner.GetChild(inner.ChildCount - 1) is Collapsible collapsible && collapsible.Body is { } bodyControl)
                bodyControl.AddChild(requirementsBody);

            border.AddChild(inner);
            body.AddChild(border);
            entry.AddChild(body);
            root.AddChild(entry);
        }
    }

    private void BuildExperiments()
    {
        _window!.CatalogBar.Visible = false;
        _window.CatalogScroll.Visible = false;

        var root = _window.PatientRoot;
        root.Visible = true;
        root.DisposeAllChildren();
        root.AddChild(Notice(Loc.GetString("operating-computer-no-experiments"), CautionColor));
    }

    private IEnumerable<OperatingComputerOperationState> GetCatalogOperations() =>
        _catalog?.Operations ?? Enumerable.Empty<OperatingComputerOperationState>();

    private Control OperationButton(OperatingComputerOperationState operation)
    {
        var pinned = _pins.Contains(operation.Name);
        var button = new Button
        {
            Text = $"{operation.Name}    —    {operation.Tool}",
            HorizontalExpand = true,
            ModulateSelfOverride = operation.Priority ? CautionColor : pinned ? DangerColor : PanelColor,
            Label = { FontColorOverride = TextColor },
            ToolTip = $"{operation.Description}\n{Loc.GetString("operating-computer-left-click-pin")}\n{Loc.GetString("operating-computer-right-click-info")}",
        };

        button.OnButtonDown += args =>
        {
            if (args.Event.Function == EngineKeyFunctions.UIRightClick)
            {
                _tab = 1;
                _search = operation.Name;
                Rebuild();
                args.Event.Handle();
                return;
            }

            if (args.Event.Function != EngineKeyFunctions.UIClick)
                return;

            if (!_pins.Remove(operation.Name))
                _pins.Add(operation.Name);
            Rebuild();
            args.Event.Handle();
        };

        return button;
    }

    private Control LabeledRow(string label, string value, Color? valueColor = null)
    {
        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            Margin = new Thickness(2, 1),
            HorizontalExpand = true,
        };

        row.AddChild(new Label { Text = label, MinSize = new Vector2(112, 0), FontColorOverride = MutedColor });
        var valueLabel = new Label { Text = value, FontColorOverride = valueColor ?? TextColor, HorizontalExpand = true };
        row.AddChild(valueLabel);
        return row;
    }

    private Control LabeledProgress(string label, float value, float min, float max, Color color, string text)
    {
        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            Margin = new Thickness(2, 1),
            HorizontalExpand = true,
        };

        row.AddChild(new Label { Text = label, MinSize = new Vector2(112, 0), FontColorOverride = MutedColor });

        var bar = new ProgressBar
        {
            MinValue = min,
            MaxValue = max <= min ? min + 1 : max,
            Value = value,
            HorizontalExpand = true,
            MinSize = new Vector2(160, 16),
            ForegroundStyleBoxOverride = new StyleBoxFlat { BackgroundColor = color },
        };
        row.AddChild(bar);
        row.AddChild(new Label { Text = $" {text}", MinSize = new Vector2(44, 0), FontColorOverride = TextColor });
        return row;
    }

    private static Label Text(string text, Color color) => new()
    {
        Text = text,
        FontColorOverride = color,
    };

    private Control Notice(string text, Color color)
    {
        var panel = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = color,
                ContentMarginLeftOverride = 6,
                ContentMarginTopOverride = 4,
                ContentMarginBottomOverride = 4,
            },
            Margin = new Thickness(0, 4),
        };

        panel.AddChild(new Label { Text = text, HorizontalAlignment = Control.HAlignment.Center, FontColorOverride = TextColor });
        return panel;
    }

    private static StyleBoxFlat SectionBox() => new()
    {
        BackgroundColor = Color.FromHex("#0A5C30"),
        BorderColor = Color.FromHex("#7FBF9A"),
        BorderThickness = new Thickness(1),
        ContentMarginLeftOverride = 2,
        ContentMarginRightOverride = 2,
        ContentMarginTopOverride = 2,
        ContentMarginBottomOverride = 2,
    };

    private static Color StateColor(string state) => state switch
    {
        "good" => GoodColor,
        "average" => AverageColor,
        _ => BadColor,
    };

    private static string Wrap(string text, int width)
    {
        if (text.Length <= width)
            return text;

        var builder = new StringBuilder();
        var line = 0;
        foreach (var word in text.Split(' '))
        {
            if (line > 0 && line + word.Length + 1 > width)
            {
                builder.Append('\n');
                line = 0;
            }
            else if (line > 0)
            {
                builder.Append(' ');
                line++;
            }

            builder.Append(word);
            line += word.Length;
        }

        return builder.ToString();
    }
}
