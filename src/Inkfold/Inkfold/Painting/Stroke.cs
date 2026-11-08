using System;
using System.Collections.Generic;
using SkiaSharp;

namespace Inkfold.Painting;

/// <summary>
/// A point along a stroke's smoothed path, with the brush radius there, in document pixels.
/// </summary>
public readonly record struct StrokeSample(float X, float Y, float R);

/// <summary>
/// How a stroke is painted: the tool, its color, size and opacity, and whether symmetry mirrors it.
/// </summary>
/// <param name="MirrorX">The vertical line symmetry mirrors across, or null when symmetry is off.</param>
public sealed record StrokeStyle(ToolKind Tool, SKColor Color, float Size, float Opacity, float? MirrorX, int Seed);

/// <summary>
/// A stroke as it stands: an immutable copy the render thread can draw while the pointer keeps moving.
/// </summary>
public sealed record LiveStroke(Layer Target, StrokeStyle Style, StrokeSample[] Samples, SKPoint[][] Dots, SKRect Bounds);

/// <summary>
/// Turns pointer positions into a smooth stroke. The path runs through the midpoints of the input points
/// with the points as control points, so corners round off; the width follows pressure, or speed when
/// there is no pressure, the way ink thins on a quick line.
/// </summary>
public sealed class StrokeBuilder
{
    // Spray dots come in a few sizes, each drawn in a single call.
    public static readonly float[] DotScales = [0.55f, 0.85f, 1.2f, 1.7f];

    private readonly ToolInfo _tool;
    private readonly StrokeStyle _style;
    private readonly Layer _target;
    private readonly List<StrokeSample> _samples = [];
    private readonly List<SKPoint>[] _dots;
    private readonly Random _random;
    private readonly float _baseRadius;
    private readonly float _slack;

    // The last two input points: the newest ends the next curve, the one before is its control point.
    private SKPoint _previous;
    private float _previousRadius;
    private SKPoint _beforePrevious;
    private float _beforePreviousRadius;
    private SKPoint _smoothed;
    private float _radius;
    private int _inputs;
    private ulong _lastTime;
    private float _untimed;
    private double _speed;
    private double _travel;
    private double _sprayDue;
    private SKRect _bounds = SKRect.Empty;

    public StrokeBuilder(ToolInfo tool, StrokeStyle style, Layer target)
    {
        _tool = tool;
        _style = style;
        _target = target;
        _random = new Random(style.Seed);
        _baseRadius = style.Size / 2;
        _dots = new List<SKPoint>[DotScales.Length];
        for (var i = 0; i < _dots.Length; i++)
            _dots[i] = [];

        // The line trails the finger on a short string, in screen pixels: jitter shorter than the string
        // never reaches the line, while corners keep their shape.
        _slack = tool.Kind switch
        {
            ToolKind.Pen => 3f,
            ToolKind.Wash => 4f,
            ToolKind.Marker => 2f,
            _ => 0f,
        };
    }

    /// <summary>
    /// Adds a pointer position in document pixels.
    /// </summary>
    /// <param name="pressure">The pen's pressure from 0 to 1, or null for touch and mouse.</param>
    /// <param name="viewScale">Screen pixels per document pixel, to judge speed as the hand does.</param>
    public void Add(SKPoint point, float? pressure, ulong timestamp, float viewScale)
    {
        var from = _smoothed;
        if (_inputs == 0)
        {
            _smoothed = point;
        }
        else
        {
            var slack = _slack / viewScale;
            var distance = SKPoint.Distance(_smoothed, point);
            if (distance <= slack)
                return;

            _smoothed = Lerp(_smoothed, point, (distance - slack) / distance);
        }

        var radius = TargetRadius(from, _smoothed, pressure, timestamp, viewScale);
        _radius = _inputs == 0 ? radius : _radius + ((radius - _radius) * 0.3f);
        AddInput(_smoothed, _radius);
    }

    /// <summary>
    /// Ends the stroke at the last position, tapering a pen's line.
    /// </summary>
    public void Finish(SKPoint last)
    {
        if (_inputs == 0)
            return;

        // The steadied line trails the finger; finish where the finger lifted.
        if (SKPoint.Distance(last, _smoothed) > 0.5f)
            AddInput(last, _radius);

        if (_inputs == 1)
        {
            Emit(new StrokeSample(_previous.X, _previous.Y, _previousRadius), 0);
        }
        else
        {
            var from = Mid(_previous, _beforePrevious);
            Segment(from, _previous, Mid(_previousRadius, _beforePreviousRadius), _previousRadius);
        }

        if (_tool.Tapers)
            TaperEnd();
    }

    public LiveStroke Snapshot()
    {
        var dots = new SKPoint[_dots.Length][];
        for (var i = 0; i < dots.Length; i++)
            dots[i] = _dots[i].ToArray();
        return new LiveStroke(_target, _style, _samples.ToArray(), dots, Bounds);
    }

    /// <summary>
    /// Gets the area the stroke covers, with room for soft edges, mirrored copy included.
    /// </summary>
    public SKRect Bounds
    {
        get
        {
            if (_bounds.IsEmpty)
                return SKRect.Empty;

            var bounds = _bounds;
            bounds.Inflate(StrokeRenderer.Margin(_style), StrokeRenderer.Margin(_style));
            if (_style.MirrorX is { } axis)
                bounds.Union(new SKRect((2 * axis) - bounds.Right, bounds.Top, (2 * axis) - bounds.Left, bounds.Bottom));
            return bounds;
        }
    }

    private void AddInput(SKPoint point, float radius)
    {
        if (_inputs == 0)
        {
            _previous = point;
            _previousRadius = radius;
            _inputs = 1;
            if (_tool.Kind == ToolKind.Spray)
                Spray(point, radius, _baseRadius);
            return;
        }

        if (SKPoint.Distance(point, _previous) < 0.35f)
            return;

        if (_inputs == 1)
        {
            // The first stretch is straight, up to the midpoint the first curve starts from.
            Segment(_previous, Mid(_previous, point), _previousRadius, Mid(_previousRadius, radius));
        }
        else
        {
            Curve(
                Mid(_beforePrevious, _previous), _previous, Mid(_previous, point),
                Mid(_beforePreviousRadius, _previousRadius), Mid(_previousRadius, radius));
        }

        _beforePrevious = _previous;
        _beforePreviousRadius = _previousRadius;
        _previous = point;
        _previousRadius = radius;
        _inputs++;
    }

    // The radius the brush wants at the new point, after the line moved from one point to the other.
    private float TargetRadius(SKPoint from, SKPoint to, float? pressure, ulong timestamp, float viewScale)
    {
        var factor = 1f;
        if (_tool.FollowsPressure)
        {
            if (pressure is { } p)
            {
                factor = 0.15f + (0.85f * MathF.Pow(Math.Clamp(p, 0, 1), 0.9f));
            }
            else if (_inputs > 0 && timestamp > _lastTime)
            {
                // Screen pixels per millisecond: a slow line keeps its full width, a quick one thins.
                // The distance includes points that arrived with the previous one's timestamp.
                var speed = (_untimed + SKPoint.Distance(from, to)) * viewScale / (timestamp - _lastTime);
                _untimed = 0;
                _speed = (_speed * 0.7) + (speed * 0.3);
                factor = (float)Math.Clamp(1.1 - (0.17 * _speed), 0.42, 1.05);
            }
            else
            {
                // Several points in one input event share its timestamp: their distance counts at the next one.
                if (_inputs > 0)
                    _untimed += SKPoint.Distance(from, to);
                factor = _inputs == 0 ? 1f : _radius / _baseRadius;
            }
        }

        _lastTime = timestamp;
        return _baseRadius * factor;
    }

    // A quadratic curve, cut into samples a fraction of the brush apart.
    private void Curve(SKPoint from, SKPoint control, SKPoint to, float fromRadius, float toRadius)
    {
        var length = SKPoint.Distance(from, control) + SKPoint.Distance(control, to);
        var steps = Math.Max(1, (int)MathF.Ceiling(length / Spacing(Math.Min(fromRadius, toRadius))));
        for (var i = 1; i <= steps; i++)
        {
            var t = (float)i / steps;
            var u = 1 - t;
            var x = (u * u * from.X) + (2 * u * t * control.X) + (t * t * to.X);
            var y = (u * u * from.Y) + (2 * u * t * control.Y) + (t * t * to.Y);
            Emit(new StrokeSample(x, y, fromRadius + ((toRadius - fromRadius) * t)), steps);
        }
    }

    private void Segment(SKPoint from, SKPoint to, float fromRadius, float toRadius)
    {
        if (_samples.Count == 0)
            Emit(new StrokeSample(from.X, from.Y, fromRadius), 0);

        var length = SKPoint.Distance(from, to);
        var steps = Math.Max(1, (int)MathF.Ceiling(length / Spacing(Math.Min(fromRadius, toRadius))));
        for (var i = 1; i <= steps; i++)
        {
            var t = (float)i / steps;
            Emit(new StrokeSample(from.X + ((to.X - from.X) * t), from.Y + ((to.Y - from.Y) * t), fromRadius + ((toRadius - fromRadius) * t)), steps);
        }
    }

    private static float Spacing(float radius) => Math.Clamp(radius * 0.3f, 0.75f, 6f);

    private void Emit(StrokeSample sample, int steps)
    {
        var step = 0f;
        if (_samples.Count > 0)
        {
            var last = _samples[^1];
            step = MathF.Sqrt(((sample.X - last.X) * (sample.X - last.X)) + ((sample.Y - last.Y) * (sample.Y - last.Y)));
            if (step < Spacing(sample.R) * 0.5f && steps > 0)
                return;
        }

        _travel += step;
        if (_tool.Tapers)
        {
            // A pen's line starts fine and swells to full width over its first stretch.
            var start = (float)Math.Min(1, 0.25 + (0.75 * _travel / (_style.Size * 1.4)));
            sample = sample with { R = sample.R * start };
        }

        _samples.Add(sample);
        var r = Math.Max(sample.R, 0.5f);
        Grow(new SKRect(sample.X - r, sample.Y - r, sample.X + r, sample.Y + r));

        if (_tool.Kind == ToolKind.Spray && step > 0)
            Spray(new SKPoint(sample.X, sample.Y), sample.R, step);
    }

    // Scatters dots around a point, denser toward the middle, as an airbrush lays them.
    private void Spray(SKPoint center, float radius, float distance)
    {
        _sprayDue += distance * radius * 0.12;
        while (_sprayDue >= 1)
        {
            _sprayDue--;
            double dx, dy;
            do
            {
                // Box-Muller: two normal deviates, kept inside the nozzle's circle.
                var u1 = 1.0 - _random.NextDouble();
                var u2 = _random.NextDouble();
                var magnitude = Math.Sqrt(-2 * Math.Log(u1)) * 0.45;
                dx = magnitude * Math.Cos(2 * Math.PI * u2);
                dy = magnitude * Math.Sin(2 * Math.PI * u2);
            }
            while ((dx * dx) + (dy * dy) > 1);

            var x = center.X + (float)(dx * radius);
            var y = center.Y + (float)(dy * radius);
            _dots[_random.Next(DotScales.Length)].Add(new SKPoint(x, y));
            Grow(new SKRect(x - 3, y - 3, x + 3, y + 3));
        }
    }

    // Empty bounds are (0, 0, 0, 0), and a union with them would stretch the stroke to the page's corner.
    private void Grow(SKRect area) =>
        _bounds = _bounds.IsEmpty ? area : SKRect.Union(_bounds, area);

    // Thins the last stretch of a pen's line to a point, as lifting the pen does.
    private void TaperEnd()
    {
        var length = Math.Min(_style.Size * 1.8, _travel * 0.4);
        if (length <= 0)
            return;

        var fromEnd = 0.0;
        for (var i = _samples.Count - 1; i > 0 && fromEnd < length; i--)
        {
            var sample = _samples[i];
            _samples[i] = sample with { R = sample.R * (float)(0.2 + (0.8 * fromEnd / length)) };
            var previous = _samples[i - 1];
            fromEnd += Math.Sqrt(((sample.X - previous.X) * (sample.X - previous.X)) + ((sample.Y - previous.Y) * (sample.Y - previous.Y)));
        }
    }

    private static SKPoint Lerp(SKPoint a, SKPoint b, float t) => new(a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t));

    private static SKPoint Mid(SKPoint a, SKPoint b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);

    private static float Mid(float a, float b) => (a + b) / 2;
}
