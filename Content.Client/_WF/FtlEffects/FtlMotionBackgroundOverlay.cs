using System.Numerics;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Graphics;

namespace Content.Client._WF.FtlEffects;

/// <summary>Preserves the space behind a rushing hull before the world is drawn.</summary>
public sealed partial class FtlMotionBackgroundOverlay : Overlay
{
    [Dependency] private IClyde _clyde = default!;
    private readonly FtlDepartureSystem _system;
    private IRenderTexture? _background;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowWorld;
    public override bool RequestScreenTexture => true;

    /// <summary>The most recent background, used only by the same viewport's motion pass.</summary>
    public Texture? Background => _background?.Texture;

    /// <summary>The viewport whose background is currently stored.</summary>
    public long ViewportId { get; private set; } = -1;

    public FtlMotionBackgroundOverlay(FtlDepartureSystem system)
    {
        IoCManager.InjectDependencies(this);
        _system = system;
        ZIndex = int.MaxValue;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        ViewportId = -1;
        return args.Viewport.Eye != null && _system.HasMotion(args.MapId, args.WorldAABB);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;
        var size = Vector2i.ComponentMax(args.Viewport.Size, _background?.Size ?? Vector2i.Zero);
        if (_background == null || _background.Size != size)
        {
            _background?.Dispose();
            _background = _clyde.CreateRenderTarget(size, RenderTargetColorFormat.Rgba8Srgb,
                new TextureSampleParameters { Filter = true }, nameof(FtlMotionBackgroundOverlay));
        }
        var handle = args.RenderHandle.DrawingHandleScreen;
        var target = _background;
        var texture = ScreenTexture;
        handle.RenderInRenderTarget(target, () =>
        {
            handle.SetTransform(Matrix3x2.Identity);
            handle.UseShader(null);
            handle.DrawTextureRect(texture, UIBox2.FromDimensions(Vector2.Zero, size));
        }, Color.Transparent);
        ViewportId = args.Viewport.Id;
    }

    protected override void DisposeBehavior()
    {
        _background?.Dispose();
        base.DisposeBehavior();
    }
}
