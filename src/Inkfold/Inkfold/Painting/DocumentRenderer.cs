using System;
using System.Collections.Generic;
using SkiaSharp;

namespace Inkfold.Painting;

/// <summary>
/// The press as it plays: each pressed layer shows its new paint within <see cref="Reach"/> of the fold,
/// and its paint from before beyond it.
/// </summary>
/// <param name="Before">The pressed layers, with their paint from before the press.</param>
/// <param name="Reach">How far the new paint has spread from the fold, from 0 to 1.</param>
public sealed record PressReveal(IReadOnlyDictionary<Layer, SKImage> Before, PressFold Fold, float Reach);

/// <summary>
/// Draws a document: paper, grain and layers, with the stroke in progress and the press animation.
/// </summary>
public static class DocumentRenderer
{
    public static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    // A small tile of paper texture, repeated across the page.
    private static readonly Lazy<SKImage> Grain = new(CreateGrain);

    public static void DrawPaper(SKCanvas canvas, Document document)
    {
        var page = SKRect.Create(document.Width, document.Height);
        using var paper = new SKPaint { Color = document.Paper };
        canvas.DrawRect(page, paper);

        // Filtered, so the speckle stays soft when the page is zoomed in.
        using var grain = Grain.Value.ToShader(SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, new SKSamplingOptions(SKFilterMode.Linear));
        using var texture = new SKPaint { Shader = grain };
        canvas.DrawRect(page, texture);
    }

    /// <summary>
    /// Draws the visible layers, bottom first. Call under <see cref="Document.Sync"/>.
    /// </summary>
    public static void DrawLayers(SKCanvas canvas, Document document, LiveStroke? live, PressReveal? press)
    {
        var page = SKRect.Create(document.Width, document.Height);
        foreach (var layer in document.Layers)
        {
            if (!layer.IsVisible || layer.Opacity <= 0 || layer.IsDisposed)
                continue;

            using var paint = new SKPaint { Color = SKColors.Black.WithAlpha((byte)Math.Round(layer.Opacity * 255)) };
            var painting = live is not null && live.Target == layer;
            SKImage? before = null;
            var pressing = press is not null && press.Before.TryGetValue(layer, out before) && before.Handle != IntPtr.Zero;
            if (!painting && !pressing)
            {
                canvas.DrawImage(layer.Image, page, Sampling, paint);
                continue;
            }

            // A group of its own, so the stroke blends with this layer only, as it will once committed.
            canvas.SaveLayer(page, paint);
            if (pressing)
                DrawPress(canvas, document, layer, before!, press!);
            else
                canvas.DrawImage(layer.Image, page, Sampling);

            if (painting)
                StrokeRenderer.Draw(canvas, live!);
            canvas.Restore();
        }
    }

    private static void DrawPress(SKCanvas canvas, Document document, Layer layer, SKImage before, PressReveal press)
    {
        var page = SKRect.Create(document.Width, document.Height);
        SKRect pressed;
        if (press.Fold == PressFold.Vertical)
        {
            var reach = document.Width / 2f * press.Reach;
            pressed = new SKRect((document.Width / 2f) - reach, 0, (document.Width / 2f) + reach, document.Height);
        }
        else
        {
            var reach = document.Height / 2f * press.Reach;
            pressed = new SKRect(0, (document.Height / 2f) - reach, document.Width, (document.Height / 2f) + reach);
        }

        canvas.Save();
        canvas.ClipRect(pressed);
        canvas.DrawImage(layer.Image, page, Sampling);
        canvas.Restore();

        canvas.Save();
        canvas.ClipRect(pressed, SKClipOperation.Difference);
        canvas.DrawImage(before, page, Sampling);
        canvas.Restore();
    }

    /// <summary>
    /// Renders the document into a new bitmap, for export, thumbnails, the fill and the color picker.
    /// </summary>
    public static SKBitmap RenderComposite(Document document, bool withPaper, float scale = 1)
    {
        var width = Math.Max(1, (int)Math.Round(document.Width * scale));
        var height = Math.Max(1, (int)Math.Round(document.Height * scale));
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(scale);
        lock (document.Sync)
        {
            if (withPaper)
                DrawPaper(canvas, document);
            DrawLayers(canvas, document, null, null);
        }

        return bitmap;
    }

    // Fine, warm speckle from fractal noise: its red channel becomes the alpha of a sepia tint. The
    // stitched tile repeats without seams.
    private static SKImage CreateGrain()
    {
        const int size = 256;
        using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var noise = SKShader.CreatePerlinNoiseFractalNoise(0.7f, 0.7f, 3, 11, new SKPointI(size, size));
        using var tint = SKColorFilter.CreateColorMatrix(
        [
            0, 0, 0, 0, 0.36f,
            0, 0, 0, 0, 0.29f,
            0, 0, 0, 0, 0.2f,
            0.5f, 0, 0, 0, -0.21f,
        ]);
        using var paint = new SKPaint { Shader = noise, ColorFilter = tint };
        surface.Canvas.DrawRect(0, 0, size, size, paint);
        return surface.Snapshot();
    }
}
