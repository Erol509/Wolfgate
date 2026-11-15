using System.Numerics;
using Content.Client._WF.RadarLabels;
using Robust.Client.Graphics;

namespace Content.Client.Shuttles.UI;

/// <summary>
/// Collects every tag the scope draws, then lays them out together (<see cref="WFRadarTagLayout"/>): a tag sits by
/// its contact while it has room, and tags that would overlap share one stacked list, each row with a faint line to
/// its contact, until zooming in makes room for them again.
/// </summary>
public partial class ShuttleNavControl
{
    private static readonly Color WfStackBacking = Color.Black.WithAlpha(0.55f);
    private static readonly Color WfStackMoreColor = Color.FromHex("#9aa4ad");

    private readonly WFRadarTagLayout _wfTagLayout = new();
    private readonly List<WFRadarTagLayout.Tag> _wfTags = new();
    private readonly List<WfTagDraw> _wfTagDraws = new();
    private readonly List<WfTagText> _wfTagLines = new();
    private readonly List<WfTagText> _wfTagAbove = new();

    private readonly record struct WfTagText(string Text, float Scale, Color Color, Vector2 Size);

    /// <summary>How a tag draws: its lines, anything drawn after its first line, and anything drawn only when it is alone.</summary>
    private struct WfTagDraw
    {
        public int FirstLine;
        public int LineCount;
        public int FirstAbove;
        public int AboveCount;
        public float HeadTextWidth;
        public Action<DrawingHandleScreen, Vector2>? DrawHeadExtra;
        public Action<DrawingHandleScreen, Vector2>? DrawAlone;
    }

    /// <summary>
    /// Starts a tag for a contact at <paramref name="anchor"/>, sitting with its top-left at <paramref name="position"/>
    /// while it has room. Both are in pixels. Add its lines with <see cref="WfTagLine"/>, the first being its name.
    /// </summary>
    private void WfBeginTag(Vector2 anchor, Vector2 position, int priority = 0)
    {
        _wfTags.Add(new WFRadarTagLayout.Tag { Anchor = anchor, Position = position, Priority = priority });
        _wfTagDraws.Add(new WfTagDraw { FirstLine = _wfTagLines.Count, FirstAbove = _wfTagAbove.Count });
    }

    /// <summary>Adds a line over the current tag's first line, each one higher than the last; shown only while the tag stands alone.</summary>
    private void WfTagAbove(DrawingHandleScreen handle, string text, float scale, Color color)
    {
        var size = handle.GetDimensions(Font, text, scale);
        _wfTagAbove.Add(new WfTagText(text, scale, color, size));
        var index = _wfTags.Count - 1;
        var tag = _wfTags[index];
        tag.Rise += size.Y;
        tag.Size.X = MathF.Max(tag.Size.X, size.X);
        _wfTags[index] = tag;
        var draw = _wfTagDraws[index];
        draw.AboveCount++;
        _wfTagDraws[index] = draw;
    }

    /// <summary>Adds a line under the current tag's others; the first line is the one a stack shows.</summary>
    private void WfTagLine(DrawingHandleScreen handle, string text, float scale, Color color)
    {
        var size = handle.GetDimensions(Font, text, scale);
        _wfTagLines.Add(new WfTagText(text, scale, color, size));
        var index = _wfTags.Count - 1;
        var tag = _wfTags[index];
        var draw = _wfTagDraws[index];
        if (draw.LineCount == 0)
        {
            draw.HeadTextWidth = size.X;
            tag.HeadSize += size;
        }

        draw.LineCount++;
        tag.Size = new Vector2(MathF.Max(tag.Size.X, size.X), tag.Size.Y + size.Y);
        _wfTags[index] = tag;
        _wfTagDraws[index] = draw;
    }

    /// <summary>Reserves <paramref name="width"/> pixels after the current tag's first line, drawn by <paramref name="draw"/> from that line's top-right.</summary>
    private void WfTagHeadExtra(float width, Action<DrawingHandleScreen, Vector2> draw)
    {
        var index = _wfTags.Count - 1;
        var tag = _wfTags[index];
        tag.HeadSize.X += width;
        tag.Size.X = MathF.Max(tag.Size.X, tag.HeadSize.X);
        _wfTags[index] = tag;
        var info = _wfTagDraws[index];
        info.DrawHeadExtra = draw;
        _wfTagDraws[index] = info;
    }

    /// <summary>Adds drawing the current tag does only while it stands alone, given its position.</summary>
    private void WfTagAlone(Action<DrawingHandleScreen, Vector2> draw)
    {
        var index = _wfTags.Count - 1;
        var info = _wfTagDraws[index];
        info.DrawAlone += draw;
        _wfTagDraws[index] = info;
    }

    /// <summary>Lays out and draws the tags collected this frame, over everything else on the scope.</summary>
    private void WfDrawTags(DrawingHandleScreen handle)
    {
        if (_wfTags.Count == 0)
            return;

        var moreScale = UIScale * 0.7f;
        _wfTagLayout.Gap = 3f * UIScale;
        _wfTagLayout.MoreHeight = handle.GetDimensions(Font, "+", moreScale).Y;
        _wfTagLayout.Solve(_wfTags, PixelSize);

        foreach (var group in _wfTagLayout.Groups)
        {
            if (!group.Stacked)
            {
                DrawTagAlone(handle, group.All[0]);
                continue;
            }

            var box = group.Box;
            var pad = 2f * UIScale;
            handle.DrawRect(new UIBox2(box.Left - pad, box.Bottom - pad, box.Right + pad, box.Top + pad), WfStackBacking);
            var y = box.Bottom;
            foreach (var index in group.Rows)
            {
                var tag = _wfTags[index];
                var draw = _wfTagDraws[index];
                var line = _wfTagLines[draw.FirstLine];
                var middle = y + tag.HeadSize.Y / 2f;
                var from = tag.Anchor.X > box.Right
                    ? new Vector2(box.Right + pad, middle)
                    : new Vector2(box.Left - pad, middle);
                if ((tag.Anchor - from).LengthSquared() > 16f * UIScale * UIScale)
                    handle.DrawLine(from, tag.Anchor, line.Color.WithAlpha(0.35f));

                var at = new Vector2(box.Left, y);
                handle.DrawString(Font, at, line.Text, line.Scale, line.Color);
                draw.DrawHeadExtra?.Invoke(handle, at + new Vector2(draw.HeadTextWidth, 0f));
                y += tag.HeadSize.Y;
            }

            if (group.Hidden > 0)
            {
                handle.DrawString(Font, new Vector2(box.Left, y), Loc.GetString("wf-radar-tags-more", ("count", group.Hidden)),
                    moreScale, WfStackMoreColor);
            }
        }

        _wfTags.Clear();
        _wfTagDraws.Clear();
        _wfTagLines.Clear();
        _wfTagAbove.Clear();
    }

    private void DrawTagAlone(DrawingHandleScreen handle, int index)
    {
        var tag = _wfTags[index];
        var draw = _wfTagDraws[index];
        var at = tag.Position;
        for (var i = 0; i < draw.LineCount; i++)
        {
            var line = _wfTagLines[draw.FirstLine + i];
            handle.DrawString(Font, at, line.Text, line.Scale, line.Color);
            if (i == 0)
                draw.DrawHeadExtra?.Invoke(handle, at + new Vector2(draw.HeadTextWidth, 0f));

            at.Y += line.Size.Y;
        }

        var above = tag.Position;
        for (var i = 0; i < draw.AboveCount; i++)
        {
            var line = _wfTagAbove[draw.FirstAbove + i];
            above.Y -= line.Size.Y;
            handle.DrawString(Font, above, line.Text, line.Scale, line.Color);
        }

        draw.DrawAlone?.Invoke(handle, tag.Position);
    }
}
