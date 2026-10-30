using System;
using System.Collections.Generic;
using SkiaSharp;

namespace Inkfold.Painting;

/// <summary>
/// The paint bucket: fills the area around a point that looks the same color on screen.
/// </summary>
public static unsafe class Fill
{
    // How far a channel can differ from the tapped color and still count as the same area.
    private const int Tolerance = 48;

    /// <summary>
    /// Fills onto <paramref name="target"/> the region of <paramref name="reference"/>, the visible layers
    /// without paper, connected to the point and close to its color. The region grows a pixel, so the fill
    /// tucks under the soft edge of the lines around it.
    /// </summary>
    /// <returns>The area filled, or null when the point is off the page.</returns>
    public static SKRectI? Apply(SKBitmap reference, SKBitmap target, int x, int y, SKColor color, double opacity)
    {
        var width = reference.Width;
        var height = reference.Height;
        if (x < 0 || y < 0 || x >= width || y >= height)
            return null;

        var source = (uint*)reference.GetPixels();
        var seed = source[(y * width) + x];
        var region = new bool[width * height];
        var left = x;
        var right = x;
        var top = y;
        var bottom = y;

        // Scanline fill: each span is filled whole, and the rows above and below it seeded from it.
        var pending = new Stack<(int X, int Y)>();
        pending.Push((x, y));
        while (pending.Count > 0)
        {
            var (px, py) = pending.Pop();
            var row = py * width;
            if (region[row + px] || !Same(source[row + px], seed))
                continue;

            var start = px;
            while (start > 0 && !region[row + start - 1] && Same(source[row + start - 1], seed))
                start--;
            var end = px;
            while (end < width - 1 && !region[row + end + 1] && Same(source[row + end + 1], seed))
                end++;

            for (var i = start; i <= end; i++)
                region[row + i] = true;

            left = Math.Min(left, start);
            right = Math.Max(right, end);
            top = Math.Min(top, py);
            bottom = Math.Max(bottom, py);

            foreach (var ny in (ReadOnlySpan<int>)[py - 1, py + 1])
            {
                if (ny < 0 || ny >= height)
                    continue;

                var next = ny * width;
                var inSpan = false;
                for (var i = start; i <= end; i++)
                {
                    var open = !region[next + i] && Same(source[next + i], seed);
                    if (open && !inSpan)
                        pending.Push((i, ny));
                    inSpan = open;
                }
            }
        }

        var area = new SKRectI(
            Math.Max(0, left - 1), Math.Max(0, top - 1), Math.Min(width, right + 2), Math.Min(height, bottom + 2));

        var pixels = (byte*)target.GetPixels();
        var alpha = (float)Math.Clamp(opacity, 0, 1);
        var sr = color.Red * alpha;
        var sg = color.Green * alpha;
        var sb = color.Blue * alpha;
        var sa = 255 * alpha;
        var keep = 1 - alpha;
        for (var py = area.Top; py < area.Bottom; py++)
        {
            for (var px = area.Left; px < area.Right; px++)
            {
                if (!Grown(region, width, height, px, py))
                    continue;

                // Premultiplied source-over, straight onto the layer's RGBA bytes.
                var p = pixels + (py * target.RowBytes) + (px * 4);
                p[0] = (byte)Math.Min(255, sr + (p[0] * keep) + 0.5f);
                p[1] = (byte)Math.Min(255, sg + (p[1] * keep) + 0.5f);
                p[2] = (byte)Math.Min(255, sb + (p[2] * keep) + 0.5f);
                p[3] = (byte)Math.Min(255, sa + (p[3] * keep) + 0.5f);
            }
        }

        return area;
    }

    private static bool Grown(bool[] region, int width, int height, int x, int y)
    {
        for (var dy = -1; dy <= 1; dy++)
        {
            var ny = y + dy;
            if (ny < 0 || ny >= height)
                continue;

            for (var dx = -1; dx <= 1; dx++)
            {
                var nx = x + dx;
                if (nx >= 0 && nx < width && region[(ny * width) + nx])
                    return true;
            }
        }

        return false;
    }

    private static bool Same(uint a, uint b)
    {
        if (a == b)
            return true;

        for (var shift = 0; shift < 32; shift += 8)
        {
            if (Math.Abs((int)((a >> shift) & 0xFF) - (int)((b >> shift) & 0xFF)) > Tolerance)
                return false;
        }

        return true;
    }
}
