using System;

namespace Inkfold.Painting;

/// <summary>
/// What a touch on the canvas does.
/// </summary>
public enum ToolKind
{
    Pen,
    Marker,
    Wash,
    Spray,
    Eraser,
    Fill,
    Picker,
}

/// <summary>
/// How a tool lays down paint, and the size and opacity it remembers between uses.
/// </summary>
public sealed class ToolInfo
{
    private ToolInfo(ToolKind kind, double minSize, double maxSize, double size, double opacity)
    {
        Kind = kind;
        MinSize = minSize;
        MaxSize = maxSize;
        Size = size;
        Opacity = opacity;
    }

    public ToolKind Kind { get; }

    /// <summary>
    /// Gets the smallest brush diameter, in document pixels.
    /// </summary>
    public double MinSize { get; }

    public double MaxSize { get; }

    public double Size { get; set; }

    public double Opacity { get; set; }

    /// <summary>
    /// Gets whether the tool paints strokes, rather than acting on a tap.
    /// </summary>
    public bool IsBrush => Kind is not (ToolKind.Fill or ToolKind.Picker);

    /// <summary>
    /// Gets whether the tool has a size.
    /// </summary>
    public bool HasSize => IsBrush;

    /// <summary>
    /// Gets whether pressure, or speed when there is no pressure, changes the width of a stroke.
    /// </summary>
    public bool FollowsPressure => Kind is ToolKind.Pen or ToolKind.Wash;

    /// <summary>
    /// Gets whether a stroke starts and ends thin, as a pen's does.
    /// </summary>
    public bool Tapers => Kind == ToolKind.Pen;

    public double Clamp(double size) => Math.Clamp(size, MinSize, MaxSize);

    /// <summary>
    /// Creates the tools with their starting sizes and opacities.
    /// </summary>
    public static ToolInfo[] CreateAll() =>
    [
        new(ToolKind.Pen, 2, 60, 9, 1),
        new(ToolKind.Marker, 8, 90, 30, 0.5),
        new(ToolKind.Wash, 10, 160, 56, 0.45),
        new(ToolKind.Spray, 16, 200, 80, 0.85),
        new(ToolKind.Eraser, 4, 160, 36, 1),
        new(ToolKind.Fill, 1, 1, 1, 1),
        new(ToolKind.Picker, 1, 1, 1, 1),
    ];
}
