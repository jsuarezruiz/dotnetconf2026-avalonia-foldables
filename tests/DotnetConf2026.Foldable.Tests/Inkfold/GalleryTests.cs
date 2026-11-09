using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Inkfold;
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

    [AvaloniaTheory]
    [InlineData("intact")]
    [InlineData("no layer list")]
    [InlineData("a page no drawing has")]
    [InlineData("a layer id that isn't a file name")]
    public void A_damaged_drawing_is_left_out_instead_of_crashing_the_start(string damage)
    {
        var layers = damage switch
        {
            "no layer list" => "null",
            "a layer id that isn't a file name" => """[{ "Id": "../../elsewhere", "Name": "Ink", "Visible": true, "Opacity": 1 }]""",
            _ => """[{ "Id": "a1b2c3d4e5", "Name": "Ink", "Visible": true, "Opacity": 1 }]""",
        };
        var width = damage == "a page no drawing has" ? -5 : 64;
        Directory.CreateDirectory(Path.Combine(_root, "drawing"));
        File.WriteAllText(Path.Combine(_root, "drawing", "drawing.json"), $$"""
            { "Name": "Test", "Width": {{width}}, "Height": 48, "Paper": "#F6F1E6", "Active": 0,
              "Created": "2026-11-09T09:00:00", "Modified": "2026-11-09T09:00:00", "Layers": {{layers}} }
            """);
        var gallery = new Gallery(_root);

        // The app starts on the latest drawing; a damaged one is skipped for a blank page.
        var studio = new Studio(gallery);
        Assert.Equal(damage == "intact" ? 1 : 0, gallery.List().Count);
        Assert.Equal(damage == "intact", studio.Document.Id == "drawing");
    }

    [AvaloniaFact]
    public void Deleting_the_open_drawing_while_it_saves_leaves_nothing_behind()
    {
        var studio = new Studio(new Gallery(_root));
        string? notice = null;
        studio.Notice += (_, text) => notice = text;
        var id = studio.Document.Id;

        // A change, its save still writing as the delete is confirmed in the gallery.
        studio.AddLayer();
        var saving = studio.SaveNow();
        var deleting = studio.DeleteDrawingAsync(id);
        while (!saving.IsCompleted || !deleting.IsCompleted)
        {
            Dispatcher.UIThread.RunJobs();
            System.Threading.Thread.Sleep(10);
        }

        Dispatcher.UIThread.RunJobs();
        Assert.Null(notice);
        Assert.False(Directory.Exists(Path.Combine(_root, id)));
        Assert.NotEqual(id, studio.Document.Id);
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
