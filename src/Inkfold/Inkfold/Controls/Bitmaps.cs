using System;
using System.IO;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Inkfold.Painting;
using SkiaSharp;

namespace Inkfold.Controls;

/// <summary>
/// Thumbnails, as Avalonia bitmaps.
/// </summary>
public static class Bitmaps
{
    public static unsafe WriteableBitmap ToAvalonia(SKBitmap source)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(source.Width, source.Height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Premul);
        using var buffer = bitmap.Lock();
        var rowBytes = source.Width * 4;
        for (var y = 0; y < source.Height; y++)
        {
            Buffer.MemoryCopy(
                (byte*)source.GetPixels() + (y * source.RowBytes),
                (byte*)buffer.Address + (y * buffer.RowBytes),
                buffer.RowBytes,
                rowBytes);
        }

        return bitmap;
    }

    /// <summary>
    /// Draws one layer on its paper, small.
    /// </summary>
    public static WriteableBitmap LayerThumbnail(Document document, Layer layer, int width)
    {
        var height = Math.Max(1, (int)Math.Round(width * (double)document.Height / document.Width));
        using var thumbnail = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(thumbnail))
        {
            canvas.Clear(document.Paper);
            lock (document.Sync)
            {
                if (!layer.IsDisposed)
                    canvas.DrawImage(layer.Image, new SKRect(0, 0, width, height), DocumentRenderer.Sampling);
            }
        }

        return ToAvalonia(thumbnail);
    }

    /// <summary>
    /// Reads an image file whole, so a save writing the same file later isn't blocked.
    /// </summary>
    public static Bitmap? Load(string path)
    {
        try
        {
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            return new Bitmap(stream);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
