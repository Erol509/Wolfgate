using System.Numerics;
using Content.Client.Resources;
using Content.Client.Stylesheets;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using static Robust.Client.UserInterface.StylesheetHelpers;

namespace Content.Client._WG.BulkCargo;

/// <summary>
/// Wolfgate Retro surfaces for the bulk terminal, following the soft PDA rework: warm charcoal plates, amber titles,
/// flat bordered keys and monospace readouts, with no instrument screws.
/// </summary>
public static class WGCargoBulkSkin
{
    private const string TexturePath = "/Textures/_WG/Stylesheets/Interface/WolfgateRetro/Style/";

    public static readonly Color Base = Color.FromHex("#141412");
    public static readonly Color Surface = Color.FromHex("#1C1B18");
    public static readonly Color SurfaceRaised = Color.FromHex("#23221E");
    public static readonly Color Hover = Color.FromHex("#3D3B34");
    public static readonly Color Pressed = Color.FromHex("#6A4A1E");
    public static readonly Color Border = Color.FromHex("#39362D");
    public static readonly Color BorderLight = Color.FromHex("#4B4739");
    public static readonly Color TitleBar = Color.FromHex("#23221E");
    public static readonly Color HeaderBar = Color.FromHex("#141412");
    public static readonly Color Primary = Color.FromHex("#2A2925");
    public static readonly Color Accent = Color.FromHex("#F2A54A");
    public static readonly Color AccentDim = Color.FromHex("#A8702E");
    public static readonly Color Text = Color.FromHex("#EDE6D2");
    public static readonly Color TextSoft = Color.FromHex("#D9D2BF");
    public static readonly Color TextMuted = Color.FromHex("#A39E8C");
    public static readonly Color TextDim = Color.FromHex("#7E7A6C");
    public static readonly Color Good = Color.FromHex("#A6C85A");
    public static readonly Color Caution = Color.FromHex("#F2C14E");
    public static readonly Color Bad = Color.FromHex("#E0645A");

    /// <summary>Sans face for body text.</summary>
    public static Font Sans(IResourceCache cache, int size) => cache.NotoStack(size: size);

    /// <summary>Monospace face for numbers and readouts.</summary>
    public static Font Mono(IResourceCache cache, int size) => cache.GetFont("/Fonts/RobotoMono/RobotoMono-Bold.ttf", size);

    /// <summary>Display face for window and section titles.</summary>
    public static Font Display(IResourceCache cache, int size) => cache.GetFont("/Fonts/Boxfont-round/Boxfont Round.ttf", size);

    /// <summary>The PDA's soft plate texture as a nine-patch panel.</summary>
    public static StyleBoxTexture Panel(IResourceCache cache, float patch = 9)
    {
        var box = new StyleBoxTexture { Texture = cache.GetTexture(TexturePath + "window_panel.png") };
        box.SetPatchMargin(StyleBox.Margin.All, patch);
        box.SetContentMarginOverride(StyleBox.Margin.All, 4);
        return box;
    }

    /// <summary>The PDA's amber section header plate.</summary>
    public static StyleBoxTexture Header(IResourceCache cache)
    {
        return new StyleBoxTexture
        {
            Texture = cache.GetTexture(TexturePath + "window_header.png"),
            PatchMarginLeft = 10,
            PatchMarginRight = 2,
            PatchMarginTop = 6,
            PatchMarginBottom = 4,
            ContentMarginLeftOverride = 8,
            ContentMarginTopOverride = 2,
            ContentMarginRightOverride = 4,
            ContentMarginBottomOverride = 3,
        };
    }

    /// <summary>Flat box with optional border and content margins.</summary>
    public static StyleBoxFlat Box(Color background, Color? border = null, Thickness? thickness = null, Thickness? content = null)
    {
        var box = new StyleBoxFlat
        {
            BackgroundColor = background,
            BorderColor = border ?? Color.Transparent,
            BorderThickness = thickness ?? new Thickness(0),
        };
        var inner = content ?? new Thickness(0);
        box.SetContentMarginOverride(StyleBox.Margin.Left, inner.Left);
        box.SetContentMarginOverride(StyleBox.Margin.Top, inner.Top);
        box.SetContentMarginOverride(StyleBox.Margin.Right, inner.Right);
        box.SetContentMarginOverride(StyleBox.Margin.Bottom, inner.Bottom);
        return box;
    }

    /// <summary>A raised content card, as the PDA lists use.</summary>
    public static StyleBoxFlat Card() => Box(SurfaceRaised, Border, new Thickness(1), new Thickness(8, 5));

    /// <summary>Flat bordered key with hover and pressed states, replacing the metal console buttons.</summary>
    public static void Key(ContainerButton button, IResourceCache cache)
    {
        var normal = Box(Primary, BorderLight, new Thickness(1), new Thickness(7, 2));
        var hover = Box(Hover, BorderLight, new Thickness(1), new Thickness(7, 2));
        var pressed = Box(Pressed, BorderLight, new Thickness(1), new Thickness(7, 2));
        var disabled = Box(SurfaceRaised, Border, new Thickness(1), new Thickness(7, 2));
        var font = Sans(cache, 10);

        button.Stylesheet = new Stylesheet(new StyleRule[]
        {
            Element<Button>().Pseudo(ContainerButton.StylePseudoClassNormal).Prop(ContainerButton.StylePropertyStyleBox, normal),
            Element<Button>().Pseudo(ContainerButton.StylePseudoClassHover).Prop(ContainerButton.StylePropertyStyleBox, hover),
            Element<Button>().Pseudo(ContainerButton.StylePseudoClassPressed).Prop(ContainerButton.StylePropertyStyleBox, pressed),
            Element<Button>().Pseudo(ContainerButton.StylePseudoClassDisabled).Prop(ContainerButton.StylePropertyStyleBox, disabled),
            Element<Label>().Prop(Label.StylePropertyFont, font).Prop(Label.StylePropertyFontColor, Text),
            Child().Parent(Element<ContainerButton>().Pseudo(ContainerButton.StylePseudoClassPressed))
                .Child(Element<Label>()).Prop(Label.StylePropertyFontColor, Text),
            Child().Parent(Element<ContainerButton>().Pseudo(ContainerButton.StylePseudoClassDisabled))
                .Child(Element<Label>()).Prop(Label.StylePropertyFontColor, TextDim),
        });
        button.MuteSounds = true;
        button.MinHeight = Math.Max(button.MinHeight, 28);
    }
}
