using System;
using SkiaSharp;

namespace Inkfold.Painting;

/// <summary>
/// Paints strokes. The same code draws a stroke live on screen and into the layer when it ends, so what
/// you see while drawing is what you get.
/// </summary>
public static class StrokeRenderer
{
    // A bend sharper than this gets a round join; gentler ones share the edge between their quads.
    private static readonly float SmoothBend = MathF.Cos(25 * MathF.PI / 180);

    public static SKBlendMode BlendMode(ToolKind tool) => tool switch
    {
        ToolKind.Eraser => SKBlendMode.DstOut,

        // Marker over marker darkens, as felt-tip ink does; on bare paper it's plain color.
        ToolKind.Marker => SKBlendMode.Multiply,
        _ => SKBlendMode.SrcOver,
    };

    /// <summary>
    /// Gets how far soft edges spread past the brush, in document pixels.
    /// </summary>
    public static float Margin(StrokeStyle style) => style.Tool == ToolKind.Wash ? (WashSoftness(style) * 3) + 2 : 2;

    private static float WashSoftness(StrokeStyle style) => Math.Max(0.8f, style.Size * 0.03f);

    // Watercolor pools at its edges as it dries: a thin body of color inside a darker rim, softened, with
    // the pigment settling unevenly into the paper. Made from the stroke's shape as one piece, since the
    // outline path also has the edges between its quads.
    private static SKImageFilter CreateWash(StrokeStyle style)
    {
        var rimWidth = Math.Max(1f, style.Size * 0.05f);
        using var inside = SKImageFilter.CreateErode(rimWidth, rimWidth);
        using var rim = SKImageFilter.CreateBlendMode(SKBlendMode.DstOut, null, inside);
        using var thin = SKColorFilter.CreateColorMatrix(
        [
            1, 0, 0, 0, 0,
            0, 1, 0, 0, 0,
            0, 0, 1, 0, 0,
            0, 0, 0, 0.5f, 0,
        ]);
        using var body = SKImageFilter.CreateColorFilter(thin);
        using var pooled = SKImageFilter.CreateMerge([body, rim]);
        using var grainNoise = SKShader.CreatePerlinNoiseFractalNoise(0.11f, 0.11f, 2, style.Seed);
        using var grainAlpha = SKColorFilter.CreateColorMatrix(
        [
            0, 0, 0, 0, 0,
            0, 0, 0, 0, 0,
            0, 0, 0, 0, 0,
            0.9f, 0, 0, 0, 0.45f,
        ]);
        using var grainShader = SKImageFilter.CreateShader(grainNoise);
        using var grain = SKImageFilter.CreateColorFilter(grainAlpha, grainShader);
        using var settled = SKImageFilter.CreateBlendMode(SKBlendMode.DstIn, pooled, grain);
        return SKImageFilter.CreateBlur(WashSoftness(style), WashSoftness(style), settled);
    }

    /// <summary>
    /// Paints a stroke onto a canvas in document coordinates, just above the layer it belongs to.
    /// </summary>
    public static void Draw(SKCanvas canvas, LiveStroke stroke)
    {
        if (stroke.Samples.Length == 0 || stroke.Bounds.IsEmpty)
            return;

        var style = stroke.Style;

        // The stroke is drawn opaque into a group, so it never darkens where it crosses itself, and the
        // group goes onto the layer with the stroke's opacity and blend.
        using var wash = style.Tool == ToolKind.Wash ? CreateWash(style) : null;
        using var group = new SKPaint
        {
            Color = SKColors.Black.WithAlpha(ToByte(style.Opacity)),
            BlendMode = BlendMode(style.Tool),
            ImageFilter = wash,
        };

        canvas.SaveLayer(stroke.Bounds, group);
        DrawShape(canvas, stroke);
        if (style.MirrorX is { } axis)
        {
            canvas.Save();
            canvas.Translate(2 * axis, 0);
            canvas.Scale(-1, 1);
            DrawShape(canvas, stroke);
            canvas.Restore();
        }

        canvas.Restore();
    }

    private static void DrawShape(SKCanvas canvas, LiveStroke stroke)
    {
        var style = stroke.Style;
        var color = style.Color.WithAlpha(255);

        if (style.Tool == ToolKind.Spray)
        {
            using var dots = new SKPaint
            {
                Color = color,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeCap = SKStrokeCap.Round,
            };
            var dotRadius = Math.Clamp(style.Size / 70f, 0.7f, 2.6f);
            for (var i = 0; i < stroke.Dots.Length; i++)
            {
                if (stroke.Dots[i].Length == 0)
                    continue;

                dots.StrokeWidth = 2 * dotRadius * StrokeBuilder.DotScales[i];
                canvas.DrawPoints(SKPointMode.Points, stroke.Dots[i], dots);
            }

            return;
        }

        using var path = BuildOutline(stroke.Samples);
        using var fill = new SKPaint { Color = color, IsAntialias = true };
        canvas.DrawPath(path, fill);
    }

    /// <summary>
    /// Builds the outline of a variable-width line: a quad for each step and a circle at each end and at
    /// each sharp bend. All of them turn clockwise, so the nonzero fill joins them without holes.
    /// </summary>
    public static SKPath BuildOutline(StrokeSample[] samples)
    {
        var path = new SKPath { FillType = SKPathFillType.Winding };
        if (samples.Length == 0)
            return path;

        if (samples.Length == 1)
        {
            AddDot(path, samples[0]);
            return path;
        }

        // The direction of each step; a step of no length keeps the one before it.
        var count = samples.Length;
        var directions = new SKPoint[count - 1];
        var last = new SKPoint(1, 0);
        for (var i = 0; i < count - 1; i++)
        {
            var dx = samples[i + 1].X - samples[i].X;
            var dy = samples[i + 1].Y - samples[i].Y;
            var length = MathF.Sqrt((dx * dx) + (dy * dy));
            if (length > 1e-4f)
                last = new SKPoint(dx / length, dy / length);
            directions[i] = last;
        }

        // At a gentle bend both quads use the averaged normal, so they meet edge to edge.
        var shared = new SKPoint?[count];
        AddDot(path, samples[0]);
        AddDot(path, samples[count - 1]);
        for (var i = 1; i < count - 1; i++)
        {
            var before = directions[i - 1];
            var after = directions[i];
            if ((before.X * after.X) + (before.Y * after.Y) >= SmoothBend)
            {
                var nx = -(before.Y + after.Y);
                var ny = before.X + after.X;
                var length = MathF.Sqrt((nx * nx) + (ny * ny));
                shared[i] = new SKPoint(nx / length, ny / length);
            }
            else
            {
                AddDot(path, samples[i]);
            }
        }

        for (var i = 0; i < count - 1; i++)
        {
            var own = new SKPoint(-directions[i].Y, directions[i].X);
            var startNormal = shared[i] ?? own;
            var endNormal = shared[i + 1] ?? own;
            var a = samples[i];
            var b = samples[i + 1];
            var ra = Radius(a);
            var rb = Radius(b);
            Span<SKPoint> quad =
            [
                new(a.X + (startNormal.X * ra), a.Y + (startNormal.Y * ra)),
                new(b.X + (endNormal.X * rb), b.Y + (endNormal.Y * rb)),
                new(b.X - (endNormal.X * rb), b.Y - (endNormal.Y * rb)),
                new(a.X - (startNormal.X * ra), a.Y - (startNormal.Y * ra)),
            ];

            // Clockwise on screen, where y grows downward, is a positive shoelace area.
            var area = 0f;
            for (var k = 0; k < 4; k++)
            {
                var p = quad[k];
                var q = quad[(k + 1) % 4];
                area += (p.X * q.Y) - (q.X * p.Y);
            }

            if (area < 0)
                quad.Reverse();

            path.MoveTo(quad[0]);
            path.LineTo(quad[1]);
            path.LineTo(quad[2]);
            path.LineTo(quad[3]);
            path.Close();
        }

        return path;
    }

    private static void AddDot(SKPath path, StrokeSample sample) =>
        path.AddCircle(sample.X, sample.Y, Radius(sample), SKPathDirection.Clockwise);

    private static float Radius(StrokeSample sample) => Math.Max(sample.R, 0.35f);

    private static byte ToByte(double opacity) => (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255);
}
