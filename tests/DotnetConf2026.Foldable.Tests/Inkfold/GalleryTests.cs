using System;
using System.IO;
using System.Threading.Tasks;
using Inkfold.Painting;
using SkiaSharp;
using Xunit;

namespace DotnetConf2026.Foldable.Tests.Inkfold;

public sealed class GalleryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"inkfold-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    [Fact]
    public async Task Undoing_a_layer_delete_after_an_autosave_keeps_its_paint()
    {
        var gallery = new Gallery(_root);
        using var document = new Document("drawing", 64, 48, Document.DefaultPaper);
        document.Layers.Add(new Layer("Ink", 64, 48));
        var top = new Layer("Top", 64, 48);
        top.Pixels.SetPixel(10, 10, SKColors.Red);
        top.Refresh();
        document.Layers.Add(top);
        document.ActiveIndex = 1;
        await gallery.SaveAsync(document);

        // Delete the layer, let the autosave remove its file, then undo the delete.
        var history = new History(document);
        var delete = new LayerStep(top, 1, removed: true);
        delete.Redo(document);
        history.Push(delete);
        await gallery.SaveAsync(document);
        Assert.True(history.Undo());
        await gallery.SaveAsync(document);

        using var loaded = gallery.Load("drawing");
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.Layers.Count);
        Assert.Equal(SKColors.Red, loaded.Layers[1].Pixels.GetPixel(10, 10));
    }
}
