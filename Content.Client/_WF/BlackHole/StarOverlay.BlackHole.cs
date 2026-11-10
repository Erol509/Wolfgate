using System.Numerics;
using Content.Client._WF.BlackHole;
using Content.Client._WF.Lattice;
using Content.Shared._FarHorizons.StarSystem.Helpers;
using Content.Shared._WF.Lattice;
using Robust.Client.Graphics;

namespace Content.Client._FarHorizons.StarSystem;

/// <summary>Refracts the parallax behind an accreting black hole before ships are drawn.</summary>
public sealed partial class StarOverlay
{
    private const string BlackHoleShader = "WFBlackHole";

    /// <summary>Only black holes need a copy of the background for refraction.</summary>
    public override bool RequestScreenTexture => _star?.Shader == BlackHoleShader;

    /// <summary>How big Kyphrus is drawn now: swelling and falling in at Tacet, otherwise its size.</summary>
    private float _blackHoleScale = 1f;

    /// <summary>How far through its swell it is, 0 to 1, for the shader.</summary>
    private float _blackHoleSwell;

    /// <summary>Culls only black holes, using the same footprint and parallax as the shader. After it falls in there is none.</summary>
    private bool BlackHoleVisible(Star star, in OverlayDrawArgs args)
    {
        if (star.Shader != BlackHoleShader)
            return true;

        var view = _entMan.System<WFLatticeViewSystem>();
        if (view.Collapsed())
            return false;

        var seconds = view.ImplosionSeconds(args.MapUid, out _);
        _blackHoleScale = seconds is { } s ? WFLatticeImplosion.Scale(s) : 1f;
        _blackHoleSwell = seconds is { } t ? Math.Clamp(t / WFLatticeImplosion.Swell, 0f, 1f) : 0f;
        if (_blackHoleScale <= 0.001f)
            return false;

        return BlackHoleVisuals.IsVisible(star.Position, star.Radius * _blackHoleScale,
            args.Viewport.Eye?.Position.Position ?? args.WorldAABB.Center, args.WorldAABB);
    }

    /// <summary>Projects world displacements into fragment pixels, including camera rotation and zoom.</summary>
    private void PrepareBlackHoleShader(in OverlayDrawArgs args)
    {
        if (_star?.Shader != BlackHoleShader || _shaderInstance == null)
            return;

        _shaderInstance.SetParameter("parallaxFactor", BlackHoleVisuals.ParallaxFactor);
        _shaderInstance.SetParameter("solarRadiusFactor", BlackHoleVisuals.SolarRadiusFactor * _blackHoleScale);
        _shaderInstance.SetParameter("swell", _blackHoleSwell);
        _shaderInstance.SetParameter("effectRadius", BlackHoleVisuals.EffectRadius);
        _shaderInstance.SetParameter("hasBackground", ScreenTexture != null);
        _shaderInstance.SetParameter("unmooring", _blackHoleSwell > 0f ? 1f : _entMan.System<WFLatticeViewSystem>().Unmooring());
        if (ScreenTexture == null)
            return;

        var origin = args.Viewport.WorldToLocal(Vector2.Zero);
        var right = args.Viewport.WorldToLocal(Vector2.UnitX) - origin;
        var up = args.Viewport.WorldToLocal(Vector2.UnitY) - origin;
        right.Y = -right.Y;
        up.Y = -up.Y;
        _shaderInstance.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shaderInstance.SetParameter("worldToScreenX", right);
        _shaderInstance.SetParameter("worldToScreenY", up);
    }
}
