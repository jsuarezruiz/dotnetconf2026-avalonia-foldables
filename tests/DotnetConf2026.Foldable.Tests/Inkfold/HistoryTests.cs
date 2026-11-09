using Inkfold.Painting;
using SkiaSharp;
using Xunit;

namespace DotnetConf2026.Foldable.Tests.Inkfold;

public class HistoryTests
{
    [Fact]
    public void An_undo_area_rounds_out_so_a_strokes_edges_are_kept()
    {
        // A soft dab's bounds rarely land on whole pixels: the partly covered ones at each edge count too.
        var area = Pixels.Clip(new SKRect(10.4f, 20.6f, 30.2f, 40.1f), new SKRectI(0, 0, 100, 100));

        Assert.Equal(new SKRectI(10, 20, 31, 41), area);
    }

    [Fact]
    public void A_deleted_layer_comes_back_whole_after_its_add_leaves_the_history()
    {
        using var document = new Document("drawing", 32, 32, Document.DefaultPaper);
        document.Layers.Add(new Layer("Ink", 32, 32));
        var history = new History(document);

        // Add a layer, then delete it.
        var layer = new Layer("Layer 2", 32, 32);
        var add = new LayerStep(layer, 1, removed: false);
        add.Redo(document);
        history.Push(add);
        var delete = new LayerStep(layer, 1, removed: true);
        delete.Redo(document);
        history.Push(delete);

        // Enough later steps that the add leaves the history (it keeps 100), but the delete stays.
        var ink = document.Layers[0];
        for (var i = 0; i < 99; i++)
        {
            var area = new SKRectI(0, 0, 1, 1);
            history.Push(new PixelsStep(ink, area, Pixels.Copy(ink.Pixels, area), Pixels.Copy(ink.Pixels, area)));
        }

        // Undo back to before the delete: the layer returns, still usable.
        for (var i = 0; i < 100; i++)
            Assert.True(history.Undo());

        Assert.Contains(layer, document.Layers);
        Assert.False(layer.IsDisposed);
        Assert.False(history.CanUndo);
    }
}
