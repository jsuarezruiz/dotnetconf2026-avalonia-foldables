using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;

namespace Inkfold.Painting;

/// <summary>
/// A saved drawing, as the gallery lists it.
/// </summary>
public sealed record GalleryItem(string Id, string Name, DateTime Modified, string ThumbnailPath);

/// <summary>
/// The drawings saved on the device: a folder each, with a PNG per layer, a thumbnail and a JSON file
/// for the rest. Only layers that changed since the last save are written again.
/// </summary>
public sealed class Gallery
{
    private const string DrawingFileName = "drawing.json";
    private const string ThumbnailFileName = "thumbnail.png";

    private readonly SemaphoreSlim _saving = new(1, 1);

    public Gallery(string root)
    {
        Root = root;
        Directory.CreateDirectory(root);
    }

    public static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Inkfold", "Drawings");

    public string Root { get; }

    /// <summary>
    /// Lists the saved drawings, most recently changed first.
    /// </summary>
    public IReadOnlyList<GalleryItem> List()
    {
        var items = new List<GalleryItem>();
        foreach (var folder in Directory.EnumerateDirectories(Root))
        {
            try
            {
                var file = ReadDrawing(folder);
                if (file is not null)
                    items.Add(new GalleryItem(Path.GetFileName(folder), file.Name, file.Modified, Path.Combine(folder, ThumbnailFileName)));
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
            {
                // A drawing that can't be read is left out rather than stopping the gallery.
            }
        }

        return [.. items.OrderByDescending(item => item.Modified)];
    }

    public Document? Load(string id)
    {
        var folder = Path.Combine(Root, id);
        var file = ReadDrawing(folder);
        if (file is null)
            return null;

        var document = new Document(id, file.Width, file.Height, ParseColor(file.Paper))
        {
            Name = file.Name,
            Created = file.Created,
            Modified = file.Modified,
        };

        foreach (var entry in file.Layers)
        {
            var path = Path.Combine(folder, LayerFileName(entry.Id));
            var pixels = Layer.CreatePixels(file.Width, file.Height);
            if (File.Exists(path))
            {
                using var stored = SKBitmap.Decode(path);
                if (stored is not null)
                {
                    using var canvas = new SKCanvas(pixels);
                    canvas.DrawBitmap(stored, 0, 0);
                }
            }

            document.Layers.Add(new Layer(entry.Name, pixels)
            {
                Id = entry.Id,
                IsVisible = entry.Visible,
                Opacity = entry.Opacity,
            });
        }

        if (document.Layers.Count == 0)
            document.Layers.Add(new Layer("Ink", file.Width, file.Height));

        document.ActiveIndex = Math.Clamp(file.Active, 0, document.Layers.Count - 1);
        foreach (var layer in document.Layers)
            layer.SavedVersion = layer.Version;
        return document;
    }

    /// <summary>
    /// Saves the drawing: the pixels are copied under the document's lock and written on another thread.
    /// Any thread can call it.
    /// </summary>
    public async Task SaveAsync(Document document)
    {
        await _saving.WaitAsync().ConfigureAwait(false);
        try
        {
            var changed = new List<(Layer Layer, int Version, SKBitmap Pixels)>();
            DrawingFile file;
            lock (document.Sync)
            {
                if (document.IsDisposed)
                    return;

                file = new DrawingFile
                {
                    Name = document.Name,
                    Width = document.Width,
                    Height = document.Height,
                    Paper = $"#{document.Paper.Red:X2}{document.Paper.Green:X2}{document.Paper.Blue:X2}",
                    Active = document.ActiveIndex,
                    Created = document.Created,
                    Modified = document.Modified,
                    Layers = [.. document.Layers.Select(layer => new LayerFile
                    {
                        Id = layer.Id,
                        Name = layer.Name,
                        Visible = layer.IsVisible,
                        Opacity = layer.Opacity,
                    })],
                };

                foreach (var layer in document.Layers)
                {
                    if (layer.SavedVersion != layer.Version)
                        changed.Add((layer, layer.Version, layer.Pixels.Copy()));
                }
            }

            using var thumbnail = DocumentRenderer.RenderComposite(document, true, 0.25f);
            var folder = Path.Combine(Root, document.Id);
            try
            {
                await Task.Run(() =>
                {
                    Directory.CreateDirectory(folder);
                    foreach (var (layer, _, pixels) in changed)
                        WritePng(pixels, Path.Combine(folder, LayerFileName(layer.Id)));

                    WritePng(thumbnail, Path.Combine(folder, ThumbnailFileName));
                    var json = Path.Combine(folder, DrawingFileName);
                    File.WriteAllText(json + ".tmp", JsonSerializer.Serialize(file, GalleryJson.Default.DrawingFile));
                    File.Move(json + ".tmp", json, true);

                    // Layers deleted since the last save.
                    var kept = file.Layers.Select(layer => LayerFileName(layer.Id)).ToHashSet();
                    foreach (var path in Directory.EnumerateFiles(folder, "layer-*.png"))
                    {
                        if (!kept.Contains(Path.GetFileName(path)))
                            File.Delete(path);
                    }
                }).ConfigureAwait(false);
            }
            finally
            {
                foreach (var (_, _, pixels) in changed)
                    pixels.Dispose();
            }

            // Only once everything is on disk: a failed save writes those layers again next time.
            lock (document.Sync)
            {
                foreach (var (layer, version, _) in changed)
                    layer.SavedVersion = version;
            }
        }
        finally
        {
            _saving.Release();
        }
    }

    public void Delete(string id)
    {
        var folder = Path.Combine(Root, id);
        if (Directory.Exists(folder))
            Directory.Delete(folder, true);
    }

    private static DrawingFile? ReadDrawing(string folder)
    {
        var path = Path.Combine(folder, DrawingFileName);
        return File.Exists(path)
            ? JsonSerializer.Deserialize(File.ReadAllText(path), GalleryJson.Default.DrawingFile)
            : null;
    }

    private static void WritePng(SKBitmap bitmap, string path)
    {
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        using (var stream = File.Create(path + ".tmp"))
            data.SaveTo(stream);
        File.Move(path + ".tmp", path, true);
    }

    private static string LayerFileName(string id) => $"layer-{id}.png";

    private static SKColor ParseColor(string text) => SKColor.TryParse(text, out var color) ? color : Document.DefaultPaper;
}

internal sealed class DrawingFile
{
    public string Name { get; set; } = "";

    public int Width { get; set; }

    public int Height { get; set; }

    public string Paper { get; set; } = "";

    public int Active { get; set; }

    public DateTime Created { get; set; }

    public DateTime Modified { get; set; }

    public List<LayerFile> Layers { get; set; } = [];
}

internal sealed class LayerFile
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public bool Visible { get; set; } = true;

    public double Opacity { get; set; } = 1;
}

// Generated serialization, which iOS's ahead-of-time compilation needs.
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(DrawingFile))]
internal sealed partial class GalleryJson : JsonSerializerContext
{
}
