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

namespace Content.Client._WF.Surgery;

/// <summary>
/// Operating computer window, reproducing /tg/station's OperatingComputer TGUI: the same tabs, sections,
/// labeled vitals, body zone selector, possible-operations list and operation catalog.
/// </summary>
[UsedImplicitly]
public sealed partial class OperatingComputerBui : BoundUserInterface
{
    // Palette from tgui's operating_computer theme.
    private static readonly Color BaseColor = Color.FromHex("#005C2E");
    private static readonly Color PrimaryColor = Color.FromHex("#0A6B38");
    private static readonly Color HeaderColor = Color.FromHex("#004D26");
    private static readonly Color BorderColor = new Color(255, 255, 255, 190);
    private static readonly Color TextColor = Color.FromHex("#E8E8E8");
    private static readonly Color MutedColor = Color.FromHex("#9CC3A8");
    private static readonly Color SelectedColor = Color.FromHex("#9E0808");
    private static readonly Color CautionColor = Color.FromHex("#B85C00");
    private static readonly Color DangerColor = Color.FromHex("#9E9E00");
    private static readonly Color GoodColor = Color.FromHex("#3CB371");
    private static readonly Color AverageColor = Color.FromHex("#DAA520");
    private static readonly Color BadColor = Color.FromHex("#B22222");
    private static readonly Color ProgressBackground = new Color(0, 0, 0, 120);

    private OperatingComputerWindow? _window;
    private OperatingComputerState? _state;
    private OperatingComputerCatalogState? _catalog;

    private readonly HashSet<string> _pins = new();
    private int _tab;
    private string _search = string.Empty;
    private string _filterTool = "all tools";
    private int _sortMode;
    private bool _hideMechanic;
    private bool _hideMechanicTouched;
    private BoxContainer? _catalogList;

    /// <summary>Last state received, exposed for integration tests.</summary>
    public OperatingComputerState? CurrentState => _state;

    public OperatingComputerBui(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
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
                if (!_hideMechanicTouched && dynamicState.HasPatient)
                    _hideMechanic = !dynamicState.PatientMechanical;
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

        _window.SetSize = new Vector2(_tab == 0 ? 350 : 430, 610);
        _window.HeaderTitle.Text = Loc.GetString("operating-computer-title");
        _window.HeaderTitle.FontColorOverride = TextColor;
        _window.Backdrop.PanelOverride = WindowBox();
        _window.HeaderPanel.PanelOverride = HeaderBox();

        var body = _window.Body;
        body.DisposeAllChildren();
        _catalogList = null;

        var tabs = BuildTabs();
        body.AddChild(tabs);
        body.AddChild(new Control { MinSize = new Vector2(0, 4) });

        var host = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
        };
        body.AddChild(host);

        if (_tab == 0)
            host.AddChild(BuildPatient());
        else if (_tab == 1)
            host.AddChild(BuildCatalog());
        else
            host.AddChild(BuildExperiments());

        body.AddChild(BuildFooter());
    }

    private static StyleBoxFlat WindowBox() => new()
    {
        BackgroundColor = BaseColor,
        BorderColor = BorderColor,
        BorderThickness = new Thickness(2),
        ContentMarginLeftOverride = 0,
        ContentMarginRightOverride = 0,
        ContentMarginTopOverride = 0,
        ContentMarginBottomOverride = 0,
    };

    private static StyleBoxFlat HeaderBox() => new()
    {
        BackgroundColor = HeaderColor,
        BorderColor = BorderColor,
        BorderThickness = new Thickness(0, 0, 0, 1),
        ContentMarginLeftOverride = 0,
        ContentMarginRightOverride = 0,
        ContentMarginTopOverride = 0,
        ContentMarginBottomOverride = 0,
    };

    private static StyleBoxFlat SectionBox() => new()
    {
        BackgroundColor = PrimaryColor,
        BorderColor = new Color(255, 255, 255, 90),
        BorderThickness = new Thickness(1),
        ContentMarginLeftOverride = 5,
        ContentMarginRightOverride = 5,
        ContentMarginTopOverride = 5,
        ContentMarginBottomOverride = 5,
    };

    private Control BuildTabs()
    {
        var bar = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
        };

        var tabs = new List<(string Text, int Index)>
        {
            (Loc.GetString("operating-computer-tab-patient"), 0),
            (Loc.GetString("operating-computer-tab-catalog"), 1),
        };

        if (_state!.HasTechweb)
            tabs.Add((Loc.GetString("operating-computer-tab-experiments"), 2));

        foreach (var (text, index) in tabs)
        {
            var selected = _tab == index;
            var button = new Button
            {
                Text = text,
                HorizontalExpand = true,
                MinSize = new Vector2(0, 22),
                ModulateSelfOverride = selected ? BaseColor : PrimaryColor,
                Label = { FontColorOverride = TextColor },
            };

            button.OnPressed += _ =>
            {
                _tab = index;
                Rebuild();
            };

            bar.AddChild(button);
        }

        return bar;
    }

    private Control BuildPatient()
    {
        var panel = Section(SectionBox());

        if (!_state!.HasTable)
        {
            AddToSection(panel, Notice(Loc.GetString("operating-computer-no-table"), CautionColor));
            return panel;
        }

        if (!_state.HasPatient)
        {
            AddToSection(panel, Notice(Loc.GetString("operating-computer-no-patient"), BadColor));
            return panel;
        }

        var stack = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
        };
        AddToSection(panel, stack);

        stack.AddChild(LabeledRow(Loc.GetString("operating-computer-state"), _state.PatientState,
            StateColor(_state.PatientStateColor)));
        stack.AddChild(LabeledRow(Loc.GetString("operating-computer-blood-type"), _state.BloodType));

        stack.AddChild(LabeledProgress(Loc.GetString("operating-computer-health"), _state.Health,
            _state.MinHealth, _state.MaxHealth, GoodColor,
            $"{MathF.Round(_state.Health / MathF.Max(1f, _state.MaxHealth) * 100f)}%"));

        var bloodRatio = _state.StandardBloodLevel > 0f ? _state.BloodLevel / _state.StandardBloodLevel : 0f;
        stack.AddChild(LabeledProgress(Loc.GetString("operating-computer-blood-level"), _state.BloodLevel, 0f,
            _state.StandardBloodLevel, bloodRatio >= 0.7f ? GoodColor : bloodRatio >= 0.45f ? AverageColor : BadColor,
            $"{MathF.Round(bloodRatio * 100f)}%"));

        stack.AddChild(LabeledProgress(Loc.GetString("operating-computer-brute"), _state.Brute, 0f,
            _state.MaxHealth, BadColor, MathF.Round(_state.Brute).ToString()));
        stack.AddChild(LabeledProgress(Loc.GetString("operating-computer-burn"), _state.Burn, 0f,
            _state.MaxHealth, BadColor, MathF.Round(_state.Burn).ToString()));
        stack.AddChild(LabeledProgress(Loc.GetString("operating-computer-toxin"), _state.Toxin, 0f,
            _state.MaxHealth, BadColor, MathF.Round(_state.Toxin).ToString()));
        stack.AddChild(LabeledProgress(Loc.GetString("operating-computer-respiratory"), _state.Oxy, 0f,
            _state.MaxHealth, BadColor, MathF.Round(_state.Oxy).ToString()));

        stack.AddChild(new Control { MinSize = new Vector2(0, 5) });
        stack.AddChild(BuildZoneAndStateRow());
        stack.AddChild(BuildPossibleOperations());
        return panel;
    }

    private Control BuildZoneAndStateRow()
    {
        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            Margin = new Thickness(0, 4),
        };

        var selector = new BodyZoneSelectorControl();
        selector.SetSelected((TargetBodyPart) _state!.Zone);
        selector.ZoneChosen += zone => SendMessage(new OperatingComputerChangeZoneMessage((int) zone));
        row.AddChild(selector);

        var states = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(8, 0, 0, 0),
        };

        foreach (var line in _state.SurgeryState)
            states.AddChild(Text($"- {line}", MutedColor));

        row.AddChild(states);
        return row;
    }

    private Control BuildPossibleOperations()
    {
        var tools = new List<string> { "all tools" };
        foreach (var operation in AvailableOperations())
        {
            foreach (var tool in operation.Tool.Split(" / "))
            {
                if (tool.Length > 0 && !tools.Contains(tool))
                    tools.Add(tool);
            }
        }

        if (!tools.Contains(_filterTool))
            _filterTool = tools[0];

        var filter = new Button
        {
            Text = Capitalize(_filterTool),
            MinSize = new Vector2(100, 0),
            ModulateSelfOverride = _filterTool == tools[0] ? PrimaryColor : SelectedColor,
            Label = { FontColorOverride = TextColor },
            ToolTip = Loc.GetString("operating-computer-filter-tool"),
        };

        filter.OnPressed += _ =>
        {
            var index = tools.IndexOf(_filterTool);
            _filterTool = tools[(index + 1) % tools.Count];
            Rebuild();
        };

        var section = Section(SectionBox(), Loc.GetString("operating-computer-next-operations"), filter);
        section.VerticalExpand = true;

        var scroll = new ScrollContainer { VerticalExpand = true, HorizontalExpand = true };
        var list = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
        };
        scroll.AddChild(list);
        AddToSection(section, scroll);

        var next = AvailableOperations()
            .Where(operation => _filterTool == tools[0] || operation.Tool.Contains(_filterTool, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(operation => _pins.Contains(operation.Name))
            .ThenByDescending(operation => operation.Priority)
            .ThenBy(operation => operation.Name)
            .ToList();

        if (next.Count == 0)
            list.AddChild(Notice(Loc.GetString("operating-computer-no-operations"), SelectedColor));

        foreach (var operation in next)
            list.AddChild(OperationButton(operation));

        return section;
    }

    private Control BuildCatalog()
    {
        var section = Section(SectionBox());
        section.VerticalExpand = true;

        var header = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            Margin = new Thickness(0, 0, 0, 4),
        };

        var search = new LineEdit
        {
            PlaceHolder = Loc.GetString("operating-computer-search"),
            Text = _search,
            MinSize = new Vector2(160, 0),
            HorizontalExpand = true,
        };
        search.OnTextChanged += args =>
        {
            _search = args.Text;
            RefreshCatalogList();
        };
        header.AddChild(search);

        var hide = new Button
        {
            Text = Loc.GetString("operating-computer-hide-mechanic"),
            MinSize = new Vector2(100, 0),
            ToggleMode = true,
            Pressed = _hideMechanic,
            ModulateSelfOverride = _hideMechanic ? SelectedColor : PrimaryColor,
            Label = { FontColorOverride = TextColor },
            ToolTip = Loc.GetString("operating-computer-hide-mechanic"),
        };
        hide.OnToggled += args =>
        {
            _hideMechanic = args.Pressed;
            _hideMechanicTouched = true;
            RefreshCatalogList();
        };
        header.AddChild(hide);

        var sort = new Button
        {
            Text = SortLabel(),
            MinSize = new Vector2(70, 0),
            ModulateSelfOverride = PrimaryColor,
            Label = { FontColorOverride = TextColor },
        };
        sort.OnPressed += _ =>
        {
            _sortMode = (_sortMode + 1) % 3;
            sort.Text = SortLabel();
            RefreshCatalogList();
        };
        header.AddChild(sort);

        AddToSection(section, header);

        var scroll = new ScrollContainer
        {
            VerticalExpand = true,
            HorizontalExpand = true,
        };
        _catalogList = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
        };
        scroll.AddChild(_catalogList);
        AddToSection(section, scroll);

        RefreshCatalogList();
        return section;
    }

    private void RefreshCatalogList()
    {
        if (_catalogList == null)
            return;

        _catalogList.DisposeAllChildren();

        var operations = CatalogOperations()
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
            _catalogList.AddChild(CatalogEntry(operation));
        }
    }

    private Control CatalogEntry(OperatingComputerOperationState operation)
    {
        var entry = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            Margin = new Thickness(0, 0, 0, 8),
        };

        var header = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
        };

        var pinned = _pins.Contains(operation.Name);
        var pin = new Button
        {
            Text = "Pin",
            MinSize = new Vector2(38, 0),
            ModulateSelfOverride = pinned ? SelectedColor : PrimaryColor,
            Label = { FontColorOverride = TextColor },
        };
        pin.OnPressed += _ =>
        {
            if (!_pins.Remove(operation.Name))
                _pins.Add(operation.Name);
            Rebuild();
        };
        header.AddChild(pin);

        var name = new Label
        {
            Text = $" {operation.Name}",
            FontColorOverride = TextColor,
            StyleClasses = { "LabelHeading" },
            VerticalAlignment = Control.VAlignment.Center,
            ClipText = true,
            HorizontalExpand = true,
        };
        header.AddChild(name);

        if (operation.Locked)
        {
            header.AddChild(new Label
            {
                Text = $"  ({Loc.GetString("operating-computer-locked")})",
                FontColorOverride = DangerColor,
                VerticalAlignment = Control.VAlignment.Center,
            });
        }

        header.AddChild(new Control { HorizontalExpand = true });
        header.AddChild(new Label
        {
            Text = operation.Tool,
            FontColorOverride = MutedColor,
            StyleClasses = { "LabelSmall" },
            VerticalAlignment = Control.VAlignment.Center,
            ClipText = true,
            MaxSize = new Vector2(150, 0),
        });
        entry.AddChild(header);

        var bodyBox = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.Transparent,
                BorderColor = MutedColor,
                BorderThickness = new Thickness(0, 0, 2, 2),
                ContentMarginLeftOverride = 4,
                ContentMarginTopOverride = 2,
            },
            Margin = new Thickness(42, 2, 0, 0),
        };

        var inner = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
        };
        inner.AddChild(new Label
        {
            Text = Wrap(operation.Description, 46),
            FontColorOverride = MutedColor,
            StyleClasses = { "LabelHeading" },
        });

        var requirements = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(6, 2, 0, 0),
        };
        foreach (var requirement in operation.Requirements)
        {
            if (requirement.Length > 0 && requirement[0] == '!')
                requirements.AddChild(new Label
                {
                    Text = requirement.Substring(1),
                    FontColorOverride = TextColor,
                    StyleClasses = { "LabelHeading" },
                    Margin = new Thickness(0, 2, 0, 0),
                });
            else
                requirements.AddChild(Text($"- {requirement}", MutedColor));
        }

        var collapsible = new Collapsible(Loc.GetString("operating-computer-requirements"),
            new CollapsibleBody())
        {
            BodyVisible = pinned,
        };
        collapsible.Body!.AddChild(requirements);
        inner.AddChild(collapsible);

        bodyBox.AddChild(inner);
        entry.AddChild(bodyBox);
        return entry;
    }

    private Control BuildExperiments()
    {
        var section = Section(SectionBox());
        AddToSection(section, Notice(Loc.GetString("operating-computer-no-experiments"), CautionColor));
        return section;
    }

    private Control BuildFooter()
    {
        return new Label
        {
            Text = Loc.GetString("operating-computer-footer"),
            FontColorOverride = MutedColor,
            StyleClasses = { "LabelSmall" },
            HorizontalAlignment = Control.HAlignment.Right,
            Margin = new Thickness(0, 4, 0, 0),
        };
    }

    private Control OperationButton(OperatingComputerOperationState operation)
    {
        var pinned = _pins.Contains(operation.Name);
        var button = new Button
        {
            Text = $"{(operation.Priority ? "! " : string.Empty)}{operation.Name}    —    {operation.Tool}",
            HorizontalExpand = true,
            ModulateSelfOverride = operation.Priority ? CautionColor : pinned ? DangerColor : PrimaryColor,
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

    private IEnumerable<OperatingComputerOperationState> CatalogOperations() =>
        _catalog?.Operations ?? Enumerable.Empty<OperatingComputerOperationState>();

    private IEnumerable<OperatingComputerOperationState> AvailableOperations() =>
        _state?.NextOperations ?? Enumerable.Empty<OperatingComputerOperationState>();

    private Control LabeledRow(string label, string value, Color? valueColor = null)
    {
        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            Margin = new Thickness(0, 1),
        };

        row.AddChild(new Label { Text = label, MinSize = new Vector2(100, 0), FontColorOverride = MutedColor });
        row.AddChild(new Label
        {
            Text = value,
            FontColorOverride = valueColor ?? TextColor,
            HorizontalExpand = true,
            ClipText = true,
        });
        return row;
    }

    private Control LabeledProgress(string label, float value, float min, float max, Color color, string text)
    {
        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            Margin = new Thickness(0, 1),
        };

        row.AddChild(new Label { Text = label, MinSize = new Vector2(100, 0), FontColorOverride = MutedColor });

        var holder = new Control { HorizontalExpand = true, MinSize = new Vector2(160, 18) };
        var bar = new ProgressBar
        {
            MinValue = min,
            MaxValue = max <= min ? min + 1 : max,
            Value = value,
            BackgroundStyleBoxOverride = new StyleBoxFlat { BackgroundColor = ProgressBackground },
            ForegroundStyleBoxOverride = new StyleBoxFlat { BackgroundColor = color },
            MouseFilter = Control.MouseFilterMode.Ignore,
        };
        var overlay = new Label
        {
            Text = text,
            FontColorOverride = TextColor,
            HorizontalAlignment = Control.HAlignment.Center,
            VerticalAlignment = Control.VAlignment.Center,
            MouseFilter = Control.MouseFilterMode.Ignore,
        };
        holder.AddChild(bar);
        holder.AddChild(overlay);
        LayoutContainer.SetAnchorPreset(bar, LayoutContainer.LayoutPreset.Wide);
        LayoutContainer.SetAnchorPreset(overlay, LayoutContainer.LayoutPreset.Wide);
        row.AddChild(holder);
        return row;
    }

    private static Label Text(string text, Color color) => new()
    {
        Text = text,
        FontColorOverride = color,
        ClipText = true,
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

        panel.AddChild(new Label
        {
            Text = text,
            HorizontalAlignment = Control.HAlignment.Center,
            FontColorOverride = TextColor,
        });
        return panel;
    }

    /// <summary>A tgui Section: a bordered panel with an optional title and right-aligned header buttons.</summary>
    private sealed class SectionPanel : PanelContainer
    {
        public BoxContainer? Root;
    }

    private static SectionPanel Section(StyleBoxFlat box, string? title = null, params Control[] buttons)
    {
        var panel = new SectionPanel { PanelOverride = box };

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
        };
        panel.AddChild(root);

        if (title == null && buttons.Length == 0)
        {
            panel.Root = root;
            return panel;
        }

        var header = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            Margin = new Thickness(0, 0, 0, 3),
        };
        header.AddChild(new Label
        {
            Text = title ?? string.Empty,
            FontColorOverride = TextColor,
            StyleClasses = { "LabelHeading" },
            HorizontalExpand = true,
            VerticalAlignment = Control.VAlignment.Center,
        });
        foreach (var button in buttons)
            header.AddChild(button);

        root.AddChild(header);
        panel.Root = root;
        return panel;
    }

    /// <summary>Adds content to a section's vertical body.</summary>
    private static void AddToSection(SectionPanel section, Control content) => section.Root!.AddChild(content);

    private string SortLabel() => _sortMode switch
    {
        1 => Loc.GetString("operating-computer-sort-name"),
        2 => Loc.GetString("operating-computer-sort-tool"),
        _ => Loc.GetString("operating-computer-sort-default"),
    };

    private static Color StateColor(string state) => state switch
    {
        "good" => GoodColor,
        "average" => AverageColor,
        _ => BadColor,
    };

    private static string Capitalize(string text)
    {
        if (text.Length == 0)
            return text;

        var chars = text.ToCharArray();
        chars[0] = char.ToUpperInvariant(chars[0]);
        return new string(chars);
    }

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
