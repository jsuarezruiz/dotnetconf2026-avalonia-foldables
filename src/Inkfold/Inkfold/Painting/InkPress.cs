using SkiaSharp;

namespace Inkfold.Painting;

/// <summary>
/// The line the page folds along: down the middle (a book) or across it (a phone turned on its side).
/// </summary>
public enum PressFold
{
    Vertical,
    Horizontal,
}

/// <summary>
/// The inkblot press: folding the page onto itself, as Rorschach did with wet ink. Each half prints onto
/// the other, mirrored across the middle of the page, where the device folds.
/// </summary>
public static class InkPress
{
    /// <summary>
    /// Presses a layer's paint onto the facing half. The print lands under the paint already there,
    /// smudged where the paper slid, spread a little where it was squeezed, and broken where the ink
    /// didn't take. Call under <see cref="Document.Sync"/>.
    /// </summary>
    public static void Apply(SKBitmap pixels, int seed, PressFold fold)
    {
        var page = SKRect.Create(pixels.Width, pixels.Height);
        using var wet = SKImage.FromBitmap(pixels);

        using var slideNoise = SKShader.CreatePerlinNoiseFractalNoise(0.018f, 0.018f, 2, seed);
        using var slide = SKImageFilter.CreateShader(slideNoise, false, page);
        using var spread = SKImageFilter.CreateDilate(1, 1);
        using var soften = SKImageFilter.CreateBlur(0.7f, 0.7f, spread);
        using var smudge = SKImageFilter.CreateDisplacementMapEffect(
            SKColorChannel.R, SKColorChannel.G, 10, slide, soften);

        // Speckle where the ink didn't take: fine noise, mostly opaque, as alpha.
        using var takeNoise = SKShader.CreatePerlinNoiseFractalNoise(0.05f, 0.05f, 2, seed + 1);
        using var takeColors = SKColorFilter.CreateColorMatrix(
        [
            0, 0, 0, 0, 0,
            0, 0, 0, 0, 0,
            0, 0, 0, 0, 0,
            5f, 0, 0, 0, -1.3f,
        ]);
        using var takeShader = SKImageFilter.CreateShader(takeNoise, false, page);
        using var take = SKImageFilter.CreateColorFilter(takeColors, takeShader);
        using var print = SKImageFilter.CreateBlendMode(SKBlendMode.SrcIn, take, smudge);

        using var paint = new SKPaint
        {
            ImageFilter = print,
            BlendMode = SKBlendMode.DstOver,
            Color = SKColors.Black.WithAlpha(225),
        };

        using var canvas = new SKCanvas(pixels);
        if (fold == PressFold.Vertical)
        {
            canvas.Translate(pixels.Width, 0);
            canvas.Scale(-1, 1);
        }
        else
        {
            canvas.Translate(0, pixels.Height);
            canvas.Scale(1, -1);
        }

        canvas.DrawImage(wet, 0, 0, paint);
    }
}
