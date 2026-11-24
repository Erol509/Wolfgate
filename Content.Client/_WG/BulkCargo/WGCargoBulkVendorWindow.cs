using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._WG.Branding;
using Content.Client.Resources;
using Content.Client.UserInterface.Controls;
using Content.Shared._NF.Bank;
using Content.Shared._WG.BulkCargo;
using Content.Shared.VendingMachines;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using static Robust.Client.UserInterface.StylesheetHelpers;

namespace Content.Client._WG.BulkCargo;

/// <summary>
/// A trade vendor's bulk crate terminal: a catalogue and cart on one view, and a live ship plot of the chosen delivery
/// zone on the other. Styled after the soft Wolfgate Retro PDA surfaces rather than the metal console housings.
/// </summary>
public sealed class WGCargoBulkVendorWindow : FancyWindow
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IResourceCache _cache = default!;
    [Dependency] private IGameTiming _timing = default!;

    public event Action<Dictionary<string, int>, Guid, int>? OnQuoteRequested;
    public event Action<Guid, int>? OnOrderPressed;
    public event Action<Guid>? OnCancelPressed;
    public event Action? OnDispensePressed;
    public event Action<string, InventoryType>? OnVendPressed;

    private const double QuoteDelay = 0.15;

    private Font _sansSmall = default!;
    private Font _sans = default!;
    private Font _mono = default!;
    private Font _monoLarge = default!;
    private Font _display = default!;

    private readonly LineEdit _search = new();
    private readonly BoxContainer _products = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 3 };
    private readonly ScrollContainer _productsScroll = new() { HScrollEnabled = false, VerticalExpand = true };
    private readonly BoxContainer _cartRows = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 3 };
    private readonly ScrollContainer _cartScroll = new() { HScrollEnabled = false, VerticalExpand = true };
    private readonly OptionButton _zones = new() { HorizontalExpand = true };
    private readonly Label _zoneNote = new() { ClipText = true };
    private readonly Label _estimateTotal = new();
    private readonly Label _estimateRange = new();
    private readonly Label _estimateNote = new() { ClipText = true };
    private readonly Label _notice = new() { ClipText = true };
    private readonly Button _orderButton;
    private readonly Button _clearButton;
    private readonly Button _dispenseButton;
    private readonly BoxContainer _ordersBox = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4 };
    private readonly ScrollContainer _ordersScroll = new() { HScrollEnabled = false, VerticalExpand = true, MinHeight = 90 };

    private readonly WGBulkDeliveryPlot _plot = new();
    private readonly Label _previewZone = new() { ClipText = true };
    private Label _previewSpace = new();
    private Label _previewReady = new();
    private Label _previewBlocked = new();
    private Label _previewLanded = new();
    private Label _previewTransit = new();
    private Label _previewEta = new();
    private readonly RichTextLabel _previewHelp = new();
    private readonly ProgressBar _previewBar = new() { MinValue = 0, MaxValue = 100, Value = 0, MinHeight = 12 };

    private BoxContainer _orderPage = default!;
    private BoxContainer _deliveryPage = default!;
    private Button _orderKey = default!;
    private Button _deliveryKey = default!;

    private WGCargoBulkState? _state;
    private readonly List<WGCargoBulkOrderEntry> _orders = new();
    private int _balanceAmount;
    private Guid _selectedZone;
    private Label _balance = default!;
    private Label _maxLabel = default!;

    private readonly List<string> _cartOrder = new();
    private readonly Dictionary<string, int> _cart = new();
    private readonly Dictionary<string, WGCargoBulkQuoteLine> _lineQuotes = new();
    private Guid _quoteId;
    private int _quoteTotal;
    private int _quoteCrates;
    private string? _quoteError;

    private bool _quoteDirty;
    private double _quoteWait;
    private int _quoteRevision;
    private bool _rebuildingZones;
    private bool _rebuildingProducts;
    private string? _productKey;
    private string? _zoneKey;
    private TimeSpan _placingUntil;

    public WGCargoBulkVendorWindow()
    {
        IoCManager.InjectDependencies(this);

        _sansSmall = WGCargoBulkSkin.Sans(_cache, 9);
        _sans = WGCargoBulkSkin.Sans(_cache, 10);
        _mono = WGCargoBulkSkin.Mono(_cache, 11);
        _monoLarge = WGCargoBulkSkin.Mono(_cache, 13);
        _display = WGCargoBulkSkin.Display(_cache, 12);

        Title = Loc.GetString("wg-bulk-window-title");
        WGWindowBranding.Apply(this);
        SolidBackdrop();
        MinSize = new Vector2(900, 540);
        var available = UserInterfaceManager.RootControl.Size - new Vector2(24);
        SetSize = Vector2.Max(MinSize, Vector2.Min(new Vector2(1160, 700), available));

        _orderButton = Key("wg-bulk-order");
        _clearButton = Key("wg-bulk-clear");
        _dispenseButton = Key("wg-bulk-dispense");

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(10, 8, 10, 10),
            SeparationOverride = 8,
            VerticalExpand = true,
        };
        ContentsContainer.AddChild(root);
        root.AddChild(Header());
        root.AddChild(Navigation());
        root.AddChild(_orderPage = OrderPage());
        root.AddChild(_deliveryPage = DeliveryPage());

        _zones.OnItemSelected += args =>
        {
            _zones.SelectId(args.Id);
            if (_rebuildingZones || _state is null)
                return;

            if (args.Button.SelectedMetadata is Guid zone)
                SelectZone(zone);
        };

        _orderButton.OnPressed += _ =>
        {
            if (!CanPlace())
                return;

            _placingUntil = _timing.RealTime + TimeSpan.FromSeconds(3);
            UpdateOrderButton();
            OnOrderPressed?.Invoke(_quoteId, _quoteTotal);
        };
        _clearButton.OnPressed += _ => ClearCart();
        _dispenseButton.OnPressed += _ => OnDispensePressed?.Invoke();

        ShowTab(order: true);
        UpdateOrderButton();
        UpdateEstimate();
        UpdatePreview();
    }

    /// <summary>Replaces the window's translucent glass with solid Retro surfaces and hides its plain divider.</summary>
    private void SolidBackdrop()
    {
        void Paint(Control control)
        {
            foreach (var child in control.Children)
            {
                if (child is PanelContainer panel)
                {
                    if (panel.HasStyleClass("WindowHeadingBackground"))
                    {
                        panel.PanelOverride = WGCargoBulkSkin.Box(WGCargoBulkSkin.TitleBar, WGCargoBulkSkin.Border,
                            new Thickness(0, 0, 0, 1));
                    }
                    else if (panel.HasStyleClass("AngleRect"))
                    {
                        panel.PanelOverride = WGCargoBulkSkin.Box(WGCargoBulkSkin.Base, WGCargoBulkSkin.Border,
                            new Thickness(1));
                    }
                    else if (panel.HasStyleClass("LowDivider"))
                    {
                        panel.Visible = false;
                    }
                }

                Paint(child);
            }
        }

        Paint(this);
    }

    private Control Header()
    {
        var header = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
            Margin = new Thickness(0, 0, 0, 2),
        };

        header.AddChild(Chip("wg-bulk-chip-bank", out _balance));
        header.AddChild(Chip("wg-bulk-chip-limit", out _maxLabel));
        header.AddChild(new Label
        {
            Text = Loc.GetString("wg-bulk-hint"),
            FontOverride = _sansSmall,
            FontColorOverride = WGCargoBulkSkin.TextMuted,
            HorizontalExpand = true,
            Align = Label.AlignMode.Right,
            VerticalAlignment = Control.VAlignment.Center,
        });
        return header;
    }

    private Control Chip(string captionKey, out Label value)
    {
        var panel = new PanelContainer
        {
            PanelOverride = WGCargoBulkSkin.Box(WGCargoBulkSkin.SurfaceRaised, WGCargoBulkSkin.Border,
                new Thickness(1), new Thickness(8, 4)),
            Margin = new Thickness(0, 0, 4, 0),
        };
        var body = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 0,
        };
        body.AddChild(new Label
        {
            Text = Loc.GetString(captionKey),
            FontOverride = _sansSmall,
            FontColorOverride = WGCargoBulkSkin.TextMuted,
        });
        value = new Label
        {
            FontOverride = _monoLarge,
            FontColorOverride = WGCargoBulkSkin.Accent,
        };
        body.AddChild(value);
        panel.AddChild(body);
        return panel;
    }

    private Control Navigation()
    {
        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 2,
        };
        _orderKey = NavKey("wg-bulk-tab-order", true);
        _deliveryKey = NavKey("wg-bulk-tab-delivery", false);
        row.AddChild(_orderKey);
        row.AddChild(_deliveryKey);
        return row;
    }

    private Button NavKey(string key, bool order)
    {
        var button = new Button
        {
            Text = Loc.GetString(key),
            MinHeight = 30,
            MinWidth = 150,
        };
        WGCargoBulkSkin.Key(button, _cache);
        button.OnPressed += _ => ShowTab(order);
        return button;
    }

    private void ShowTab(bool order)
    {
        if (_orderPage is null || _deliveryPage is null)
            return;

        _orderPage.Visible = order;
        _deliveryPage.Visible = !order;

        var active = WGCargoBulkSkin.Box(WGCargoBulkSkin.SurfaceRaised, WGCargoBulkSkin.AccentDim,
            new Thickness(0, 0, 0, 2), new Thickness(7, 2));
        var idle = WGCargoBulkSkin.Box(WGCargoBulkSkin.Base, WGCargoBulkSkin.Border,
            new Thickness(0, 0, 0, 1), new Thickness(7, 2));
        _orderKey.StyleBoxOverride = order ? active : idle;
        _deliveryKey.StyleBoxOverride = order ? idle : active;
    }

    private BoxContainer OrderPage()
    {
        var page = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 10,
            VerticalExpand = true,
        };
        page.AddChild(Catalogue());
        page.AddChild(SideColumn());
        return page;
    }

    private BoxContainer DeliveryPage()
    {
        var page = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 10,
            VerticalExpand = true,
        };
        page.AddChild(Section("wg-bulk-panel-preview", _plot, expand: true));
        page.AddChild(PreviewColumn());
        page.Visible = false;
        return page;
    }

    private Control Section(string titleKey, Control content, bool expand = false)
    {
        var panel = new PanelContainer
        {
            PanelOverride = WGCargoBulkSkin.Panel(_cache),
            HorizontalExpand = true,
            VerticalExpand = expand,
        };

        var body = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = expand,
        };

        var header = new PanelContainer
        {
            PanelOverride = WGCargoBulkSkin.Header(_cache),
            HorizontalExpand = true,
            MinHeight = 28,
        };
        var headerRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            MinHeight = 28,
        };
        headerRow.AddChild(new Label
        {
            Text = Loc.GetString(titleKey),
            FontOverride = _display,
            FontColorOverride = WGCargoBulkSkin.Accent,
            VerticalAlignment = Control.VAlignment.Center,
        });
        header.AddChild(headerRow);
        body.AddChild(header);

        var inner = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = expand,
            Margin = new Thickness(6, 8, 6, 8),
        };
        inner.AddChild(content);
        body.AddChild(inner);

        panel.AddChild(body);
        return panel;
    }

    private Control Catalogue()
    {
        var column = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 6,
        };

        _search.PlaceHolder = Loc.GetString("wg-bulk-search");
        _search.Stylesheet = new Stylesheet(new StyleRule[]
        {
            Element<LineEdit>()
                .Prop(LineEdit.StylePropertyStyleBox,
                    WGCargoBulkSkin.Box(WGCargoBulkSkin.Surface, WGCargoBulkSkin.BorderLight,
                        new Thickness(1), new Thickness(6, 3)))
                .Prop("font", _mono)
                .Prop("font-color", WGCargoBulkSkin.Text)
                .Prop(LineEdit.StylePropertyCursorColor, WGCargoBulkSkin.Accent),
        });
        _search.OnTextChanged += _ =>
        {
            _productKey = null;
            RebuildProducts();
        };

        _productsScroll.AddChild(_products);
        column.AddChild(_search);
        column.AddChild(Section("wg-bulk-panel-catalogue", _productsScroll, expand: true));
        return column;
    }

    private Control SideColumn()
    {
        var side = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            MinWidth = 420,
            MaxWidth = 440,
            SeparationOverride = 8,
        };

        var destination = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 5,
        };
        WGCargoBulkSkin.Key(_zones, _cache);
        _zoneNote.FontOverride = _sansSmall;
        _zoneNote.FontColorOverride = WGCargoBulkSkin.TextMuted;
        destination.AddChild(_zones);
        destination.AddChild(_zoneNote);
        destination.AddChild(_dispenseButton);
        side.AddChild(Section("wg-bulk-panel-destination", destination));

        var cart = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            VerticalExpand = true,
        };
        _cartScroll.MinHeight = 90;
        _cartScroll.MaxHeight = 160;
        _cartScroll.AddChild(_cartRows);
        cart.AddChild(_cartScroll);
        cart.AddChild(EstimateCard());

        _notice.FontOverride = _sansSmall;
        _notice.FontColorOverride = WGCargoBulkSkin.Caution;
        cart.AddChild(_notice);
        var keys = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
        };
        keys.AddChild(_orderButton);
        keys.AddChild(_clearButton);
        cart.AddChild(keys);
        side.AddChild(Section("wg-bulk-panel-cart", cart));

        _ordersScroll.AddChild(_ordersBox);
        side.AddChild(Section("wg-bulk-panel-orders", _ordersScroll, expand: true));
        return side;
    }

    private Control EstimateCard()
    {
        var panel = new PanelContainer
        {
            PanelOverride = WGCargoBulkSkin.Box(WGCargoBulkSkin.Surface, WGCargoBulkSkin.AccentDim,
                new Thickness(1), new Thickness(8, 6)),
        };
        var body = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 1,
        };
        _estimateTotal.FontOverride = WGCargoBulkSkin.Display(_cache, 14);
        _estimateTotal.FontColorOverride = WGCargoBulkSkin.Accent;
        _estimateRange.FontOverride = _mono;
        _estimateRange.FontColorOverride = WGCargoBulkSkin.TextMuted;
        _estimateNote.FontOverride = _sansSmall;
        body.AddChild(_estimateTotal);
        body.AddChild(_estimateRange);
        body.AddChild(_estimateNote);
        panel.AddChild(body);
        return panel;
    }

    private Control PreviewColumn()
    {
        var stats = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            MinWidth = 300,
            MaxWidth = 330,
            SeparationOverride = 5,
        };

        _previewZone.FontOverride = WGCargoBulkSkin.Display(_cache, 13);
        _previewZone.FontColorOverride = WGCargoBulkSkin.Accent;
        stats.AddChild(_previewZone);
        stats.AddChild(StatRow("wg-bulk-preview-space-caption", out _previewSpace));
        stats.AddChild(StatRow("wg-bulk-preview-ready-caption", out _previewReady));
        stats.AddChild(StatRow("wg-bulk-preview-blocked-caption", out _previewBlocked));
        stats.AddChild(StatRow("wg-bulk-preview-landed-caption", out _previewLanded));
        stats.AddChild(new PanelContainer
        {
            PanelOverride = WGCargoBulkSkin.Box(WGCargoBulkSkin.Border, null, new Thickness(0)),
            MinHeight = 1,
            Margin = new Thickness(0, 4),
        });
        stats.AddChild(StatRow("wg-bulk-preview-transit-caption", out _previewTransit, accent: true));
        stats.AddChild(StatRow("wg-bulk-preview-eta-caption", out _previewEta, accent: true));
        stats.AddChild(_previewBar);
        stats.AddChild(_previewHelp);

        return Section("wg-bulk-panel-stats", stats);
    }

    private Control StatRow(string captionKey, out Label value, bool accent = false)
    {
        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 6,
        };
        row.AddChild(new Label
        {
            Text = Loc.GetString(captionKey),
            FontOverride = _sansSmall,
            FontColorOverride = WGCargoBulkSkin.TextMuted,
            MinWidth = 120,
        });
        value = new Label
        {
            FontOverride = _monoLarge,
            FontColorOverride = accent ? WGCargoBulkSkin.Accent : WGCargoBulkSkin.Text,
            HorizontalExpand = true,
            Align = Label.AlignMode.Right,
        };
        row.AddChild(value);
        return row;
    }

    public void UpdateState(WGCargoBulkState state)
    {
        _state = state;
        _maxLabel.Text = Loc.GetString("wg-bulk-crates", ("count", state.MaxCrates));
        RebuildProducts();

        if (_selectedZone == Guid.Empty || state.Zones.TrueForAll(zone => zone.Id != _selectedZone))
        {
            var preferred = state.Zones.FirstOrDefault(zone => !zone.Ship)?.Id
                            ?? state.Zones.FirstOrDefault()?.Id
                            ?? Guid.Empty;
            SelectZone(preferred);
        }

        RebuildZones();
        UpdateZoneNote();
        UpdateEstimate();
        UpdateOrderButton();
        UpdatePreview();
    }

    public void UpdateOrders(WGCargoBulkOrdersMessage message)
    {
        _orders.Clear();
        _orders.AddRange(message.Orders);
        RebuildOrders();
        UpdatePreview();
    }

    public void UpdateBalance(WGCargoBulkBalanceMessage message)
    {
        _balanceAmount = message.Balance;
        _balance.Text = BankSystemExtensions.ToSpesoString(_balanceAmount);
        UpdateEstimate();
        UpdateOrderButton();
    }

    public void UpdateQuote(WGCargoBulkQuoteMessage message)
    {
        // A reply for a cart or request that has since changed must not re-enable PLACE ORDER.
        if (message.Revision != _quoteRevision)
            return;

        if (message.Error == "wg-bulk-error-wait")
        {
            MarkQuoteDirty();
            return;
        }

        _quoteId = message.Quote;
        _quoteTotal = message.Total;
        _quoteCrates = message.Crates;
        _quoteError = message.Error;

        _lineQuotes.Clear();
        foreach (var line in message.Lines)
            _lineQuotes[line.Id] = line;

        RebuildCart();
        UpdateEstimate();
        UpdateOrderButton();
    }

    public void UpdateOrderResult(WGCargoBulkOrderResultMessage message)
    {
        _placingUntil = TimeSpan.Zero;

        if (message.Success && message.Placed)
            ClearCart(keepNotice: true);
        else if (!message.Success)
            MarkQuoteDirty();

        Notice(message.Success
            ? Loc.GetString(message.Placed ? "wg-bulk-order-placed" : "wg-bulk-order-cancelled")
            : Loc.GetString(message.Error ?? "wg-bulk-error-order"), message.Success);
        UpdateOrderButton();
    }

    public void UpdateDispenseResult(WGCargoBulkDispenseResultMessage message)
    {
        Notice(message.Success
            ? Loc.GetString("wg-bulk-dispensed")
            : Loc.GetString(message.Error ?? "wg-bulk-error-dispense"), message.Success);
    }

    private void Notice(string text, bool good = false)
    {
        _notice.Text = text;
        _notice.FontColorOverride = good ? WGCargoBulkSkin.Good : WGCargoBulkSkin.Caution;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_placingUntil != TimeSpan.Zero && _timing.RealTime >= _placingUntil)
        {
            _placingUntil = TimeSpan.Zero;
            UpdateOrderButton();
        }

        if (!_quoteDirty)
            return;

        _quoteWait += args.DeltaSeconds;
        if (_quoteWait < QuoteDelay)
            return;

        _quoteWait = 0;
        _quoteDirty = false;
        RequestQuote();
    }

    #region Catalogue

    private void RebuildProducts()
    {
        if (_state is null || _rebuildingProducts)
            return;

        var key = string.Join("|", _state.Products.Select(p => $"{p.Id}:{p.UnitPrice}:{p.Stock}:{p.Crate}:{p.Type}"));
        if (key == _productKey)
            return;

        _productKey = key;
        _rebuildingProducts = true;
        _products.RemoveAllChildren();

        var filter = _search.Text.Trim();
        var count = 0;
        foreach (var product in _state.Products)
        {
            var name = ProductName(product.Id);
            if (filter.Length > 0 && !name.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
                continue;

            _products.AddChild(ProductCard(product, name));
            count++;
        }

        if (count == 0)
        {
            _products.AddChild(new Label
            {
                Text = Loc.GetString("wg-bulk-catalogue-empty"),
                FontOverride = _sansSmall,
                FontColorOverride = WGCargoBulkSkin.TextMuted,
                Margin = new Thickness(8, 6),
            });
        }

        _rebuildingProducts = false;
    }

    private Control ProductCard(WGCargoBulkProduct product, string name)
    {
        var card = new PanelContainer
        {
            HorizontalExpand = true,
            PanelOverride = WGCargoBulkSkin.Card(),
            Margin = new Thickness(0, 0, 0, 3),
        };
        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 8,
        };

        var icon = new EntityPrototypeView
        {
            SetSize = new Vector2(32),
            VerticalAlignment = Control.VAlignment.Center,
        };
        icon.SetPrototype(product.Id);
        row.AddChild(icon);

        var text = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 1,
            VerticalAlignment = Control.VAlignment.Center,
        };
        text.AddChild(new Label
        {
            Text = name,
            FontOverride = _sans,
            FontColorOverride = WGCargoBulkSkin.Text,
            ClipText = true,
        });
        text.AddChild(new Label
        {
            Text = product.Crate
                ? Loc.GetString("wg-bulk-product-stock", ("count", product.Stock))
                : Loc.GetString("wg-bulk-product-supply"),
            FontOverride = _sansSmall,
            FontColorOverride = product.Crate ? WGCargoBulkSkin.TextMuted : WGCargoBulkSkin.Caution,
        });
        row.AddChild(text);

        row.AddChild(new Label
        {
            Text = product.Crate ? BankSystemExtensions.ToSpesoString(product.UnitPrice) : "—",
            FontOverride = _monoLarge,
            FontColorOverride = product.Crate ? WGCargoBulkSkin.Accent : WGCargoBulkSkin.TextDim,
            MinWidth = 88,
            Align = Label.AlignMode.Right,
            VerticalAlignment = Control.VAlignment.Center,
        });

        var keys = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 3,
            VerticalAlignment = Control.VAlignment.Center,
        };
        if (product.Crate)
        {
            keys.AddChild(AddKey("+1", product.Id, 1));
            keys.AddChild(AddKey("+5", product.Id, 5));
            keys.AddChild(AddKey("+10", product.Id, 10));
            keys.AddChild(AddKey(Loc.GetString("wg-bulk-add-max"), product.Id, -1, 50));
        }
        else
        {
            var id = product.Id;
            var type = product.Type;
            var buy = new Button { Text = Loc.GetString("wg-bulk-buy"), MinWidth = 56, MinHeight = 24 };
            WGCargoBulkSkin.Key(buy, _cache);
            buy.OnPressed += _ => OnVendPressed?.Invoke(id, type);
            keys.AddChild(buy);
        }

        row.AddChild(keys);
        card.AddChild(row);
        return card;
    }

    private Button AddKey(string label, string id, int amount, float width = 36)
    {
        var button = new Button { Text = label, MinWidth = width, MinHeight = 24 };
        WGCargoBulkSkin.Key(button, _cache);
        button.OnPressed += _ => AddToCart(id, amount);
        return button;
    }

    private string ProductName(string id)
    {
        return _proto.TryIndex<EntityPrototype>(id, out var proto) ? proto.Name : id;
    }

    #endregion

    #region Cart

    private void AddToCart(string id, int amount)
    {
        if (_state?.Products.FirstOrDefault(product => product.Id == id) is not { Crate: true } product)
            return;

        var count = _cart.GetValueOrDefault(id);
        var room = Math.Min(product.Stock, _state.MaxCrates - (_cart.Values.Sum() - count)) - count;
        var add = amount < 0 ? room : Math.Min(amount, room);
        if (add <= 0)
            return;

        if (count == 0)
            _cartOrder.Add(id);

        _cart[id] = count + add;
        MarkQuoteDirty();
        RebuildCart();
    }

    private void ChangeCount(string id, int delta)
    {
        if (!_cart.TryGetValue(id, out var count))
            return;

        var next = count + delta;
        if (next <= 0)
        {
            _cart.Remove(id);
            _cartOrder.Remove(id);
        }
        else
        {
            if (_state?.Products.FirstOrDefault(product => product.Id == id) is { } product)
            {
                var total = _cart.Values.Sum() - count + next;
                if (next > count && (next > product.Stock || total > _state.MaxCrates))
                    return;
            }

            _cart[id] = next;
        }

        MarkQuoteDirty();
        RebuildCart();
    }

    private void ClearCart(bool keepNotice = false)
    {
        _quoteRevision++;
        _cart.Clear();
        _cartOrder.Clear();
        _lineQuotes.Clear();
        _quoteId = Guid.Empty;
        _quoteTotal = 0;
        _quoteCrates = 0;
        _quoteError = null;
        RebuildCart();
        UpdateEstimate();
        UpdateOrderButton();
        if (!keepNotice)
            _notice.Text = string.Empty;
    }

    private void MarkQuoteDirty()
    {
        _quoteRevision++;
        _quoteDirty = true;
        _quoteWait = 0;
        _quoteError = null;
        _quoteId = Guid.Empty;
        UpdateOrderButton();
    }

    private void RequestQuote()
    {
        if (_cart.Count == 0)
        {
            ResetQuote();
            return;
        }

        if (_selectedZone == Guid.Empty)
        {
            _quoteError = "wg-bulk-estimate-none";
            UpdateEstimate();
            return;
        }

        OnQuoteRequested?.Invoke(new Dictionary<string, int>(_cart), _selectedZone, _quoteRevision);
    }

    private void ResetQuote()
    {
        _quoteRevision++;
        _lineQuotes.Clear();
        _quoteId = Guid.Empty;
        _quoteTotal = 0;
        _quoteCrates = 0;
        _quoteError = null;
        RebuildCart();
        UpdateEstimate();
        UpdateOrderButton();
    }

    private void RebuildCart()
    {
        _cartRows.RemoveAllChildren();

        if (_cartOrder.Count == 0)
        {
            _cartRows.AddChild(new Label
            {
                Text = Loc.GetString("wg-bulk-cart-empty"),
                FontOverride = _sansSmall,
                FontColorOverride = WGCargoBulkSkin.TextMuted,
                Margin = new Thickness(6, 4),
            });
            return;
        }

        foreach (var id in _cartOrder)
        {
            if (!_cart.TryGetValue(id, out var count))
                continue;

            var row = new PanelContainer
            {
                HorizontalExpand = true,
                PanelOverride = WGCargoBulkSkin.Card(),
            };
            var contents = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                HorizontalExpand = true,
                SeparationOverride = 2,
            };
            contents.AddChild(new Label
            {
                Text = ProductName(id),
                FontOverride = _sans,
                FontColorOverride = WGCargoBulkSkin.Text,
                ClipText = true,
            });

            var controls = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Horizontal,
                HorizontalExpand = true,
                SeparationOverride = 4,
            };
            var less = new Button { Text = "-", MinWidth = 28, MinHeight = 24 };
            WGCargoBulkSkin.Key(less, _cache);
            less.OnPressed += _ => ChangeCount(id, -1);
            var more = new Button { Text = "+", MinWidth = 28, MinHeight = 24 };
            WGCargoBulkSkin.Key(more, _cache);
            more.OnPressed += _ => ChangeCount(id, 1);
            var remove = new Button { Text = "×", MinWidth = 28, MinHeight = 24 };
            WGCargoBulkSkin.Key(remove, _cache);
            remove.OnPressed += _ =>
            {
                _cart.Remove(id);
                _cartOrder.Remove(id);
                MarkQuoteDirty();
                RebuildCart();
            };

            controls.AddChild(less);
            controls.AddChild(new Label
            {
                Text = count.ToString(),
                FontOverride = _mono,
                FontColorOverride = WGCargoBulkSkin.Text,
                MinWidth = 32,
                Align = Label.AlignMode.Center,
            });
            controls.AddChild(more);
            controls.AddChild(remove);
            controls.AddChild(new Label
            {
                Text = _lineQuotes.TryGetValue(id, out var quote)
                    ? BankSystemExtensions.ToSpesoString(quote.Subtotal)
                    : "—",
                FontOverride = _mono,
                FontColorOverride = WGCargoBulkSkin.TextSoft,
                HorizontalExpand = true,
                Align = Label.AlignMode.Right,
            });
            contents.AddChild(controls);
            row.AddChild(contents);
            _cartRows.AddChild(row);
        }
    }

    private void UpdateEstimate()
    {
        if (_cart.Count == 0)
        {
            _estimateTotal.Text = "—";
            _estimateRange.Text = string.Empty;
            _estimateNote.Text = Loc.GetString("wg-bulk-estimate-none");
            _estimateNote.FontColorOverride = WGCargoBulkSkin.TextMuted;
            return;
        }

        if (_quoteError != null)
        {
            _estimateTotal.Text = "—";
            _estimateRange.Text = string.Empty;
            _estimateNote.Text = Loc.GetString(_quoteError);
            _estimateNote.FontColorOverride = WGCargoBulkSkin.Caution;
            return;
        }

        if (_quoteId == Guid.Empty || _lineQuotes.Count == 0)
        {
            _estimateTotal.Text = "—";
            _estimateRange.Text = string.Empty;
            _estimateNote.Text = Loc.GetString("wg-bulk-estimate-pending");
            _estimateNote.FontColorOverride = WGCargoBulkSkin.TextMuted;
            return;
        }

        var first = _lineQuotes.Values.Min(line => line.First);
        var last = _lineQuotes.Values.Max(line => line.Last);
        _estimateTotal.Text = Loc.GetString("wg-bulk-estimate-total",
            ("total", BankSystemExtensions.ToSpesoString(_quoteTotal)),
            ("crates", _quoteCrates));
        _estimateRange.Text = Loc.GetString("wg-bulk-estimate-range",
            ("first", BankSystemExtensions.ToSpesoString(first)),
            ("last", BankSystemExtensions.ToSpesoString(last)));
        _estimateNote.Text = _quoteTotal > _balanceAmount
            ? Loc.GetString("wg-bulk-error-funds")
            : string.Empty;
        _estimateNote.FontColorOverride = WGCargoBulkSkin.Caution;
    }

    private bool CanPlace()
    {
        return _quoteId != Guid.Empty
               && _quoteError == null
               && _cart.Count > 0
               && _selectedZone != Guid.Empty
               && _quoteTotal <= _balanceAmount
               && _timing.RealTime >= _placingUntil;
    }

    private void UpdateOrderButton()
    {
        _orderButton.Disabled = !CanPlace();
        _clearButton.Disabled = _cart.Count == 0;
    }

    #endregion

    #region Zones and preview

    private void RebuildZones()
    {
        if (_state is null)
            return;

        // Cell lists change with every crate; the selector only shows names, so they stay out of the key.
        var key = string.Join("|", _state.Zones.Select(zone => $"{zone.Id}:{zone.Label}"));
        if (key == _zoneKey)
            return;

        _zoneKey = key;
        _rebuildingZones = true;
        _zones.Clear();

        for (var i = 0; i < _state.Zones.Count; i++)
        {
            var zone = _state.Zones[i];
            _zones.AddItem(zone.Label);
            _zones.SetItemMetadata(i, zone.Id);
            if (zone.Id == _selectedZone)
                _zones.SelectId(i);
        }

        _rebuildingZones = false;

        if (_state.Zones.TrueForAll(zone => zone.Id != _selectedZone))
        {
            var preferred = _state.Zones.FirstOrDefault(zone => !zone.Ship)?.Id
                            ?? _state.Zones.FirstOrDefault()?.Id
                            ?? Guid.Empty;
            SelectZone(preferred);
        }

        UpdateZoneNote();
    }

    private WGCargoBulkZone? SelectedZone => _state?.Zones.FirstOrDefault(zone => zone.Id == _selectedZone);

    private WGCargoBulkOrderEntry? SelectedOrder =>
        _orders.FirstOrDefault(order => order.Zone == _selectedZone && order.Delivered < order.Total)
        ?? _orders.FirstOrDefault(order => order.Zone == _selectedZone);

    private void SelectZone(Guid zone)
    {
        if (_selectedZone == zone)
            return;

        _selectedZone = zone;
        MarkQuoteDirty();
        UpdateZoneNote();
        UpdatePreview();
    }

    private void UpdateZoneNote()
    {
        _zoneNote.Text = _state is not { Zones.Count: > 0 }
            ? Loc.GetString("wg-bulk-zone-none")
            : Loc.GetString("wg-bulk-zone-free",
                ("free", SelectedZone?.Free.Count ?? 0),
                ("blocked", SelectedZone?.Blocked.Count ?? 0));
    }

    private void UpdatePreview()
    {
        var zone = SelectedZone;
        var order = SelectedOrder;
        _plot.SetZone(zone, order);

        if (_state is null || zone is null)
        {
            _previewZone.Text = Loc.GetString("wg-bulk-preview-zone-none");
            _previewZone.FontColorOverride = WGCargoBulkSkin.TextMuted;
            _previewSpace.Text = _previewReady.Text = _previewBlocked.Text = _previewLanded.Text =
                _previewTransit.Text = _previewEta.Text = "—";
            _previewHelp.SetMessage(Loc.GetString(_state is { Zones.Count: 0 } ? "wg-bulk-preview-no-zone" : "wg-bulk-preview-help"));
            _previewBar.MaxValue = 1;
            _previewBar.Value = 0;
            return;
        }

        _previewZone.Text = Loc.GetString("wg-bulk-preview-zone", ("zone", zone.Label));
        _previewZone.FontColorOverride = WGCargoBulkSkin.Accent;
        _previewSpace.Text = ((zone.Max.X - zone.Min.X + 1) * (zone.Max.Y - zone.Min.Y + 1)).ToString();
        _previewReady.Text = zone.Free.Count.ToString();
        _previewBlocked.Text = zone.Blocked.Count.ToString();
        _previewLanded.Text = zone.Landed.Count.ToString();

        if (order is { Total: > 0 })
        {
            var remaining = Math.Max(0, order.Total - order.Delivered);
            _previewTransit.Text = Loc.GetString("wg-bulk-preview-transit-value",
                ("remaining", remaining), ("total", order.Total));
            _previewEta.Text = FormatTime(OrderEta(order));
            _previewBar.MaxValue = order.Total;
            _previewBar.Value = order.Delivered;
        }
        else
        {
            _previewTransit.Text = Loc.GetString("wg-bulk-preview-none");
            _previewEta.Text = FormatTime(_state.Interval * zone.Free.Count);
            _previewBar.MaxValue = Math.Max(1, zone.Free.Count + zone.Blocked.Count + zone.Reserved.Count);
            _previewBar.Value = 0;
        }

        _previewHelp.SetMessage(Loc.GetString("wg-bulk-preview-help"));
    }

    private static TimeSpan OrderEta(WGCargoBulkOrderEntry order)
    {
        var remaining = Math.Max(0, order.Total - order.Delivered);
        if (remaining == 0)
            return TimeSpan.Zero;

        return order.NextIn + order.Interval * (remaining - 1);
    }

    private static string FormatTime(TimeSpan time)
    {
        if (time < TimeSpan.Zero)
            time = TimeSpan.Zero;

        return time.TotalHours >= 1
            ? time.ToString(@"h\:mm\:ss")
            : time.ToString(@"m\:ss");
    }

    private void RebuildOrders()
    {
        _ordersBox.RemoveAllChildren();

        if (_orders.Count == 0)
        {
            _ordersBox.AddChild(new Label
            {
                Text = Loc.GetString("wg-bulk-orders-empty"),
                FontOverride = _sansSmall,
                FontColorOverride = WGCargoBulkSkin.TextMuted,
                Margin = new Thickness(6, 4),
            });
            return;
        }

        foreach (var order in _orders)
        {
            var card = new PanelContainer
            {
                HorizontalExpand = true,
                PanelOverride = WGCargoBulkSkin.Card(),
            };
            var body = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                HorizontalExpand = true,
                SeparationOverride = 3,
            };

            var header = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Horizontal,
                HorizontalExpand = true,
                SeparationOverride = 6,
            };
            header.AddChild(new Label
            {
                Text = OrderSummary(order),
                FontOverride = _sans,
                FontColorOverride = WGCargoBulkSkin.Text,
                HorizontalExpand = true,
                ClipText = true,
            });
            var cancel = new Button { Text = Loc.GetString("wg-bulk-cancel"), MinWidth = 68, MinHeight = 24 };
            WGCargoBulkSkin.Key(cancel, _cache);
            var id = order.Id;
            cancel.OnPressed += _ => OnCancelPressed?.Invoke(id);
            header.AddChild(cancel);
            body.AddChild(header);

            var progress = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Horizontal,
                HorizontalExpand = true,
                SeparationOverride = 6,
            };
            progress.AddChild(new Label
            {
                Text = Loc.GetString("wg-bulk-order-progress",
                    ("delivered", order.Delivered), ("total", order.Total)),
                FontOverride = _sansSmall,
                FontColorOverride = WGCargoBulkSkin.TextMuted,
                MinWidth = 86,
            });
            progress.AddChild(new ProgressBar
            {
                MinValue = 0,
                MaxValue = Math.Max(1, order.Total),
                Value = order.Delivered,
                MinHeight = 7,
                HorizontalExpand = true,
                VerticalAlignment = Control.VAlignment.Center,
            });
            body.AddChild(progress);
            body.AddChild(new Label
            {
                Text = Loc.GetString("wg-bulk-order-zone-eta",
                    ("zone", order.ZoneLabel), ("eta", FormatTime(OrderEta(order)))),
                FontOverride = _sansSmall,
                FontColorOverride = WGCargoBulkSkin.TextMuted,
            });

            card.AddChild(body);
            _ordersBox.AddChild(card);
        }
    }

    private string OrderSummary(WGCargoBulkOrderEntry order)
    {
        return string.Join(", ", order.Lines.Select(line => $"{ProductName(line.Id)} ×{line.Count}"));
    }

    private Button Key(string locKey)
    {
        var button = new Button { Text = Loc.GetString(locKey), HorizontalExpand = true };
        WGCargoBulkSkin.Key(button, _cache);
        return button;
    }

    #endregion
}
