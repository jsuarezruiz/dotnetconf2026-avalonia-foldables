using System.Linq;
using Inkfold.Painting;
using SkiaSharp;
using Xunit;

namespace DotnetConf2026.Foldable.Tests.Inkfold;

public class StrokeBuilderTests
{
    [Fact]
    public void A_quick_pen_line_is_thinner_than_a_slow_one()
    {
        var slow = LastRadius(Line(pixelsPerMillisecond: 0.5f));
        var quick = LastRadius(Line(pixelsPerMillisecond: 4f));

        Assert.True(quick < slow * 0.7f, $"quick {quick}, slow {slow}");
    }

    [Fact]
    public void A_quick_line_thins_just_as_much_when_its_points_come_in_batches()
    {
        // The same quick line, one point per 10 ms, or four points per 40 ms event sharing its timestamp.
        var single = LastRadius(Line(pixelsPerMillisecond: 4f));
        using var layer = new Layer("Ink", 64, 64);
        var batched = new StrokeBuilder(Tool(ToolKind.Pen), Style(ToolKind.Pen, 20), layer);
        for (var i = 0; i < 40; i++)
            batched.Add(new SKPoint(100 + (i * 4f * 10), 100), null, (ulong)(1000 + ((i / 4) * 40)), 1f);

        Assert.InRange(LastRadius(batched), single * 0.8f, single * 1.25f);
    }

    [Fact]
    public void A_spray_stroke_is_bounded_where_it_was_drawn()
    {
        using var layer = new Layer("Ink", 1600, 1180);
        var builder = new StrokeBuilder(Tool(ToolKind.Spray), Style(ToolKind.Spray, 40), layer);
        for (var i = 0; i < 10; i++)
            builder.Add(new SKPoint(800 + (i * 5), 600), null, (ulong)(i * 16), 1f);

        var bounds = builder.Bounds;
        Assert.True(bounds.Left > 700 && bounds.Top > 500, $"bounds {bounds}");
    }

    // A straight pen line, a point every 10 ms, with no pressure as from touch or a mouse.
    private static StrokeBuilder Line(float pixelsPerMillisecond)
    {
        using var layer = new Layer("Ink", 64, 64);
        var builder = new StrokeBuilder(Tool(ToolKind.Pen), Style(ToolKind.Pen, 20), layer);
        for (var i = 0; i < 40; i++)
            builder.Add(new SKPoint(100 + (i * pixelsPerMillisecond * 10), 100), null, (ulong)(1000 + (i * 10)), 1f);
        return builder;
    }

    private static float LastRadius(StrokeBuilder builder) => builder.Snapshot().Samples[^1].R;

    private static ToolInfo Tool(ToolKind kind) => ToolInfo.CreateAll().First(tool => tool.Kind == kind);

    private static StrokeStyle Style(ToolKind kind, float size) => new(kind, SKColors.Black, size, 1, null, 7);
}
