using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Inkfold.Painting;
using SkiaSharp;

namespace Inkfold.Controls;

/// <summary>
/// A sample stroke with the brush in hand, painted by the same code as the page.
/// </summary>
public sealed class BrushPreview : Control
{
    // Strokes need a layer to belong to; the preview's is never painted on.
    private static readonly Layer Sheet = new("Preview", 1, 1);

    private Studio? _studio;
    private LiveStroke? _stroke;

    public Studio? Studio
    {
        get => _studio;
        set
        {
            if (_studio is { } old)
                old.SettingsChanged -= OnSettingsChanged;
            _studio = value;
            if (value is not null)
                value.SettingsChanged += OnSettingsChanged;
            Rebuild();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty)
            Rebuild();
    }

    public override void Render(DrawingContext context)
    {
        if (_studio is not { } studio)
            return;

        context.Custom(new PreviewDrawing(new Rect(Bounds.Size), studio.Document.Paper, _stroke));
    }

    private void OnSettingsChanged(object? sender, EventArgs e) => Rebuild();

    // An S across the box, quick in the middle, so a pen shows how it thins.
    private void Rebuild()
    {
        _stroke = null;
        if (_studio is { Tool.IsBrush: true } studio && Bounds.Width > 40 && Bounds.Height > 20)
        {
            var width = (float)Bounds.Width;
            var height = (float)Bounds.Height;
            var tool = studio.Tool;
            var size = (float)Math.Min(tool.Size * 0.6, height * 0.62);
            var style = new StrokeStyle(tool.Kind, studio.Color, size, (float)tool.Opacity, null, 7);
            var builder = new StrokeBuilder(tool, style, Sheet);
            var margin = Math.Max(size * 0.6f, 14);
            var time = 0UL;
            SKPoint last = default;
            for (var i = 0; i <= 48; i++)
            {
                var t = i / 48f;
                last = new SKPoint(
                    margin + ((width - (2 * margin)) * t),
                    (height / 2) + (MathF.Sin(t * MathF.PI * 2) * (height / 2 - margin) * 0.75f));
                time += (ulong)(6 + (14 * MathF.Abs(MathF.Cos(t * MathF.PI))));
                builder.Add(last, null, time, 1);
            }

            builder.Finish(last);
            _stroke = builder.Snapshot();
        }

        InvalidateVisual();
    }

    private sealed class PreviewDrawing(Rect bounds, SKColor paper, LiveStroke? stroke) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;

        public bool HitTest(Point p) => false;

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose()
        {
        }

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } leaseFeature)
                return;

            using var lease = leaseFeature.Lease();
            var canvas = lease.SkCanvas;
            var box = new SKRect(0, 0, (float)bounds.Width, (float)bounds.Height);
            using var paint = new SKPaint { Color = paper, IsAntialias = true };
            canvas.DrawRoundRect(box, 14, 14, paint);
            if (stroke is null)
                return;

            canvas.Save();
            canvas.ClipRoundRect(new SKRoundRect(box, 14), antialias: true);
            canvas.SaveLayer(box, null);
            if (stroke.Style.Tool == ToolKind.Eraser)
            {
                // Something to erase: a band of soft gray hatching.
                using var hatch = new SKPaint { Color = new SKColor(0x8A, 0x84, 0x99), IsAntialias = true, StrokeWidth = 3 };
                for (var x = -box.Height; x < box.Width; x += 9)
                    canvas.DrawLine(x, box.Height, x + box.Height, 0, hatch);
            }

            StrokeRenderer.Draw(canvas, stroke);
            canvas.Restore();
            canvas.Restore();
        }
    }
}
