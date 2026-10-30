using System;
using SkiaSharp;

namespace Inkfold.Painting;

/// <summary>
/// One sheet of paint: pixels the tools change, and an image of them the screen draws.
/// </summary>
public sealed class Layer : IDisposable
{
    public Layer(string name, int width, int height)
        : this(name, CreatePixels(width, height))
    {
    }

    public Layer(string name, SKBitmap pixels)
    {
        Name = name;
        Pixels = pixels;
        Image = SKImage.FromBitmap(pixels);
    }

    /// <summary>
    /// Gets a stable name for the layer's file in the gallery.
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..10];

    public string Name { get; set; }

    public bool IsVisible { get; set; } = true;

    public double Opacity { get; set; } = 1;

    /// <summary>
    /// Gets the premultiplied RGBA pixels. Change them only under <see cref="Document.Sync"/>, then call <see cref="Refresh"/>.
    /// </summary>
    public SKBitmap Pixels { get; }

    /// <summary>
    /// Gets an immutable copy of <see cref="Pixels"/>. It keeps its GPU texture until the pixels change,
    /// so the screen uploads only the layer being painted.
    /// </summary>
    public SKImage Image { get; private set; }

    /// <summary>
    /// Gets a number that changes whenever the pixels do.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>
    /// Gets or sets the <see cref="Version"/> last written to disk, so saving skips layers that haven't changed.
    /// </summary>
    public int SavedVersion { get; set; } = -1;

    public bool IsDisposed { get; private set; }

    /// <summary>
    /// Renews <see cref="Image"/> after the pixels change. Call under <see cref="Document.Sync"/>.
    /// </summary>
    public void Refresh()
    {
        Pixels.NotifyPixelsChanged();
        var old = Image;
        Image = SKImage.FromBitmap(Pixels);
        old.Dispose();
        Version++;
    }

    public Layer Clone(string name) =>
        new(name, Pixels.Copy()) { IsVisible = IsVisible, Opacity = Opacity };

    public void Dispose()
    {
        if (IsDisposed)
            return;

        IsDisposed = true;
        Image.Dispose();
        Pixels.Dispose();
    }

    public static SKBitmap CreatePixels(int width, int height)
    {
        var pixels = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        pixels.Erase(SKColors.Transparent);
        return pixels;
    }
}
