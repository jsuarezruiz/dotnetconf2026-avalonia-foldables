using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace Inkfold.Painting;

/// <summary>
/// A drawing: its paper and a stack of layers, bottom first.
/// </summary>
public sealed class Document : IDisposable
{
    // Two pages side by side: the spread of the iPhone Duo's inner display, either side of its fold.
    public const int DefaultWidth = 1600;
    public const int DefaultHeight = 1180;

    public static readonly SKColor DefaultPaper = new(0xF6, 0xF1, 0xE6);

    public Document(string id, int width, int height, SKColor paper)
    {
        Id = id;
        Width = width;
        Height = height;
        Paper = paper;
    }

    /// <summary>
    /// Gets the lock held while layers change and while the screen draws them, which it does on the render thread.
    /// </summary>
    public object Sync { get; } = new();

    public string Id { get; }

    public string Name { get; set; } = "Untitled";

    public int Width { get; }

    public int Height { get; }

    public SKRectI Bounds => new(0, 0, Width, Height);

    public SKColor Paper { get; }

    public List<Layer> Layers { get; } = [];

    public int ActiveIndex { get; set; }

    public Layer Active => Layers[ActiveIndex];

    public DateTime Created { get; set; } = DateTime.Now;

    public DateTime Modified { get; set; } = DateTime.Now;

    public bool IsDisposed { get; private set; }

    public static Document CreateBlank()
    {
        var document = new Document(NewId(), DefaultWidth, DefaultHeight, DefaultPaper)
        {
            Name = $"Drawing {DateTime.Now:MMM d, h:mm tt}",
        };
        document.Layers.Add(new Layer("Ink", DefaultWidth, DefaultHeight));
        return document;
    }

    public static string NewId() => $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}";

    /// <summary>
    /// Gets a name for a new layer that no layer has yet.
    /// </summary>
    public string NextLayerName()
    {
        for (var n = Layers.Count + 1; ; n++)
        {
            var name = $"Layer {n}";
            if (Layers.All(layer => layer.Name != name))
                return name;
        }
    }

    public void Dispose()
    {
        lock (Sync)
        {
            IsDisposed = true;
            foreach (var layer in Layers)
                layer.Dispose();
            Layers.Clear();
        }
    }
}
