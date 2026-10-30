using System;
using SkiaSharp;

namespace Inkfold.Painting;

/// <summary>
/// Copies rectangles of pixels in and out of a layer, for undo.
/// </summary>
public static unsafe class Pixels
{
    /// <summary>
    /// Rounds an area out to whole pixels, inside the page.
    /// </summary>
    public static SKRectI Clip(SKRect area, SKRectI page)
    {
        var rounded = SKRectI.Ceiling(area);
        return SKRectI.Intersect(rounded, page);
    }

    public static SKBitmap Copy(SKBitmap source, SKRectI area)
    {
        var copy = new SKBitmap(new SKImageInfo(area.Width, area.Height, source.ColorType, source.AlphaType));
        var from = (byte*)source.GetPixels();
        var to = (byte*)copy.GetPixels();
        var rowBytes = area.Width * source.BytesPerPixel;
        for (var y = 0; y < area.Height; y++)
        {
            Buffer.MemoryCopy(
                from + ((area.Top + y) * source.RowBytes) + (area.Left * source.BytesPerPixel),
                to + (y * copy.RowBytes),
                rowBytes,
                rowBytes);
        }

        return copy;
    }

    public static void Paste(SKBitmap region, SKBitmap target, int left, int top)
    {
        var from = (byte*)region.GetPixels();
        var to = (byte*)target.GetPixels();
        var rowBytes = region.Width * region.BytesPerPixel;
        for (var y = 0; y < region.Height; y++)
        {
            Buffer.MemoryCopy(
                from + (y * region.RowBytes),
                to + ((top + y) * target.RowBytes) + (left * target.BytesPerPixel),
                rowBytes,
                rowBytes);
        }
    }
}
