using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Inkfold.Controls;

/// <summary>
/// A line icon from <see cref="Icons"/>, stroked in the foreground color it inherits.
/// </summary>
public sealed class Icon : Control
{
    /// <summary>
    /// Defines the <see cref="Data"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> DataProperty =
        AvaloniaProperty.Register<Icon, string?>(nameof(Data));

    /// <summary>
    /// Defines the <see cref="Foreground"/> property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<Icon>();

    private static readonly Dictionary<string, Geometry> Parsed = [];

    static Icon()
    {
        AffectsRender<Icon>(DataProperty, ForegroundProperty);
    }

    public string? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Data is not { } data || Foreground is not { } brush)
            return;

        if (!Parsed.TryGetValue(data, out var geometry))
        {
            geometry = StreamGeometry.Parse(data);
            Parsed[data] = geometry;
        }

        // The icons are drawn on a 24-unit grid with 2-unit round strokes.
        var size = Math.Min(Bounds.Width, Bounds.Height);
        var scale = size / 24;
        var offset = new Vector((Bounds.Width - size) / 2, (Bounds.Height - size) / 2);
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offset)))
            context.DrawGeometry(null, new Pen(brush, 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geometry);
    }
}
