using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using Inkfold.Animation;
using Inkfold.Painting;
using Nova.Avalonia.Foldable;
using SkiaSharp;

namespace Inkfold.Controls;

/// <summary>
/// The page you paint on. One finger, a pen or the mouse paints with the tool in hand; two fingers pan
/// and pinch, and a quick two-finger tap undoes (three redoes). The mouse wheel zooms.
/// </summary>
/// <remarks>
/// Across a vertical fold the page lies open like a book: the band of the fold is cut out of the view,
/// so the page's two halves sit either side of it, and the middle of the page, where the press mirrors,
/// is the fold itself.
/// </remarks>
public sealed class PaintCanvas : Control
{
    /// <summary>
    /// Defines the <see cref="FitInsets"/> property.
    /// </summary>
    public static readonly StyledProperty<Thickness> FitInsetsProperty =
        AvaloniaProperty.Register<PaintCanvas, Thickness>(nameof(FitInsets));

    private const double FitMargin = 12;
    private const double MaxScale = 8;
    private const ulong TapTime = 280;
    private const double TapSlop = 12;

    // How long a stroke can run before a second finger stops counting as the start of a pinch.
    private const ulong PinchGrace = 300;

    private readonly Dictionary<IPointer, Point> _touches = [];
    private readonly FrameTicker _pressTicker;

    private Studio? _studio;

    // The view. Virtual coordinates are local ones with the fold's band cut out.
    private double _scale = 1;
    private Point _origin;
    private bool _fitted = true;
    private bool _hasBand;
    private double _bandStart;
    private double _bandWidth;

    // A stroke, fill or pick in progress.
    private StrokeBuilder? _stroke;
    private IPointer? _toolPointer;
    private ulong _toolStart;
    private Point _toolDown;
    private Point _toolAt;
    private bool _toolMoved;
    private SKBitmap? _pickSource;
    private ulong? _penLiftedAt;
    private const ulong PenIdleMilliseconds = 5000;

    // Two-finger navigation, and a mouse drag with the right or middle button.
    private PinchState? _pinch;
    private IPointer? _panPointer;
    private Point _panLast;
    private Point? _hover;

    private PressReveal? _press;
    private object? _pressSync;

    // The view easing back to fit.
    private readonly FrameTicker _viewTicker;
    private (double Scale, Point Origin) _viewFrom;

    // A dab of the brush, shown for a moment while its size or opacity changes.
    private readonly DispatcherTimer _dabTimer;
    private bool _showDab;

    public PaintCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
        _pressTicker = new FrameTicker(this, OnPressFrame);
        _viewTicker = new FrameTicker(this, OnViewFrame);
        _dabTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _dabTimer.Tick += (_, _) =>
        {
            _dabTimer.Stop();
            _showDab = false;
            InvalidateVisual();
        };
    }

    /// <summary>
    /// Gets or sets the room overlays take at the edges, which fitting the page leaves clear.
    /// </summary>
    public Thickness FitInsets
    {
        get => GetValue(FitInsetsProperty);
        set => SetValue(FitInsetsProperty, value);
    }

    public Studio? Studio
    {
        get => _studio;
        set
        {
            if (_studio is { } old)
            {
                old.PaintChanged -= OnPaintChanged;
                old.SettingsChanged -= OnPaintChanged;
                old.LayersChanged -= OnPaintChanged;
                old.DocumentOpened -= OnDocumentOpened;
                old.Pressed -= OnPressed;
                old.BrushAdjusted -= OnBrushAdjusted;
            }

            _studio = value;
            if (value is not null)
            {
                value.PaintChanged += OnPaintChanged;
                value.SettingsChanged += OnPaintChanged;
                value.LayersChanged += OnPaintChanged;
                value.DocumentOpened += OnDocumentOpened;
                value.Pressed += OnPressed;
                value.BrushAdjusted += OnBrushAdjusted;
            }

            FitToView();
        }
    }

    /// <summary>
    /// Gets whether the whole page is in view, as it is until a pinch or the wheel zooms.
    /// </summary>
    public bool IsFitted => _fitted;

    /// <summary>
    /// Raised when <see cref="IsFitted"/> changes.
    /// </summary>
    public event EventHandler? FittedChanged;

    /// <summary>
    /// Fits the whole page in view, centered on the fold when there is one; eased when asked for, at
    /// once when the screen changes.
    /// </summary>
    public void FitToView(bool animate = false)
    {
        SetFitted(true);
        if (_studio is null || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        if (animate)
        {
            _viewFrom = (_scale, _origin);
            _viewTicker.Start();
            return;
        }

        _viewTicker.Stop();
        (_scale, _origin) = FitTarget();
        InvalidateVisual();
    }

    /// <summary>
    /// Zooms by a factor around a point in local coordinates.
    /// </summary>
    public void ZoomAt(Point local, double factor)
    {
        if (_studio is null)
            return;

        _viewTicker.Stop();
        var page = ToPage(local);
        var scale = Math.Clamp(_scale * factor, MinScale, MaxScale);
        var at = ToVirtual(local);
        _origin = new Point(at.X - (page.X * scale), at.Y - (page.Y * scale));
        _scale = scale;
        SetFitted(false);
        InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty || change.Property == Fold.StateProperty || change.Property == FitInsetsProperty)
        {
            UpdateBand();
            if (_fitted)
                FitToView();
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        if (_studio is not { } studio)
            return;

        var tool = studio.Tool;
        var hoverRadius = _hover is not null && tool.HasSize && _stroke is null ? tool.Size / 2 * _scale : 0;
        var pick = _pickSource is not null ? (Point?)_toolAt : null;

        // The dab sits in the middle of the first page, clear of the fold.
        Dab? dab = null;
        if (_showDab && tool.HasSize)
        {
            var width = _hasBand ? _bandStart : Bounds.Width;
            dab = new Dab(
                new Point(width / 2, Bounds.Height / 2),
                Math.Max(2, tool.Size / 2 * _scale),
                (float)tool.Opacity,
                tool.Kind == ToolKind.Eraser);
        }

        context.Custom(new PageDrawing(
            new Rect(Bounds.Size),
            studio.Document,
            _stroke?.Snapshot(),
            _press,
            Segments(),
            studio.Symmetry,
            _hasBand ? (_bandStart, _bandWidth) : null,
            _hover,
            hoverRadius,
            pick,
            dab,
            studio.Color,
            _scale));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_studio is not { } studio)
            return;

        var point = e.GetCurrentPoint(this);
        var type = e.Pointer.Type;
        Focus();

        if (type == PointerType.Mouse && (point.Properties.IsRightButtonPressed || point.Properties.IsMiddleButtonPressed))
        {
            _panPointer = e.Pointer;
            _panLast = point.Position;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (type == PointerType.Pen)
            _penLiftedAt = e.Timestamp;

        if (type == PointerType.Touch)
        {
            _touches[e.Pointer] = point.Position;
            e.Pointer.Capture(this);

            // While a pen is in use, fingers only move the page, so a resting hand doesn't paint.
            if (_touches.Count > 1 || IsPenInUse(e.Timestamp))
            {
                if (_pinch is null)
                    StartPinch(e.Timestamp);
                else
                    MovePinch();
                e.Handled = true;
                return;
            }
        }

        if (_toolPointer is not null)
            return;

        _toolPointer = e.Pointer;
        _toolStart = e.Timestamp;
        _toolDown = point.Position;
        _toolAt = point.Position;
        _toolMoved = false;
        e.Pointer.Capture(this);
        e.Handled = true;

        switch (studio.Tool.Kind)
        {
            case ToolKind.Picker:
                _pickSource = DocumentRenderer.RenderComposite(studio.Document, true);
                Pick(point.Position);
                break;
            case ToolKind.Fill:
                break;
            default:
                _stroke = studio.BeginStroke();
                AddToStroke(point, e.Timestamp);
                break;
        }

        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = e.GetCurrentPoint(this);

        if (e.Pointer == _panPointer)
        {
            _origin += point.Position - _panLast;
            _panLast = point.Position;
            SetFitted(false);
            InvalidateVisual();
            return;
        }

        if (_touches.ContainsKey(e.Pointer))
        {
            _touches[e.Pointer] = point.Position;
            if (_pinch is not null)
            {
                MovePinch();
                return;
            }
        }

        if (e.Pointer != _toolPointer)
        {
            if (e.Pointer.Type == PointerType.Mouse && _toolPointer is null)
            {
                _hover = point.Position;
                InvalidateVisual();
            }

            return;
        }

        _toolAt = point.Position;
        if (Distance(_toolAt, _toolDown) > TapSlop)
            _toolMoved = true;

        if (_stroke is not null)
        {
            // Points the platform collected between events, for smooth lines at speed.
            var points = e.GetIntermediatePoints(this);
            for (var i = 0; i < points.Count; i++)
                AddToStroke(points[i], e.Timestamp);
        }
        else if (_pickSource is not null)
        {
            Pick(point.Position);
        }

        if (e.Pointer.Type == PointerType.Mouse)
            _hover = point.Position;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var point = e.GetCurrentPoint(this);

        if (e.Pointer.Type == PointerType.Pen)
            _penLiftedAt = e.Timestamp;

        if (e.Pointer == _panPointer)
        {
            _panPointer = null;
            e.Pointer.Capture(null);
            return;
        }

        if (_touches.Remove(e.Pointer) && _pinch is not null)
        {
            EndPinch(e.Timestamp);
            e.Pointer.Capture(null);
            return;
        }

        if (e.Pointer != _toolPointer)
            return;

        _toolAt = point.Position;
        FinishTool(commit: true);
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _touches.Remove(e.Pointer);
        if (e.Pointer == _panPointer)
            _panPointer = null;
        if (e.Pointer == _toolPointer)
            FinishTool(commit: true);
        if (_pinch is not null && _touches.Count == 0)
            _pinch = null;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = null;
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        ZoomAt(e.GetPosition(this), Math.Pow(1.0015, e.Delta.Y * 120));
        e.Handled = true;
    }

    private double MinScale => _studio is { } studio
        ? Math.Min(VirtualWidth / studio.Document.Width, Bounds.Height / studio.Document.Height) * 0.4
        : 0.05;

    private double VirtualWidth => Bounds.Width - (_hasBand ? _bandWidth : 0);

    private void AddToStroke(PointerPoint point, ulong timestamp)
    {
        if (_stroke is null)
            return;

        var page = ToPage(point.Position);
        float? pressure = point.Pointer.Type == PointerType.Pen ? point.Properties.Pressure : null;
        _stroke.Add(new SKPoint((float)page.X, (float)page.Y), pressure, timestamp, (float)_scale);
    }

    private void FinishTool(bool commit)
    {
        var studio = _studio;
        _toolPointer = null;

        if (_stroke is { } stroke)
        {
            _stroke = null;
            if (commit && studio is not null)
            {
                var page = ToPage(_toolAt);
                stroke.Finish(new SKPoint((float)page.X, (float)page.Y));
                studio.Commit(stroke);
            }
        }
        else if (_pickSource is { } source)
        {
            _pickSource = null;
            source.Dispose();
            if (commit)
                studio?.FinishPick();
        }
        else if (commit && studio is not null && studio.Tool.Kind == ToolKind.Fill && !_toolMoved)
        {
            var page = ToPage(_toolAt);
            studio.FillAt(new SKPoint((float)page.X, (float)page.Y));
        }

        InvalidateVisual();
    }

    private void Pick(Point local)
    {
        if (_pickSource is not { } source || _studio is not { } studio)
            return;

        var page = ToPage(local);
        var x = (int)page.X;
        var y = (int)page.Y;
        if (x >= 0 && y >= 0 && x < source.Width && y < source.Height)
            studio.SetColor(source.GetPixel(x, y));
    }

    private void StartPinch(ulong timestamp)
    {
        // A stroke a moment old was the first finger of the pinch, not a line.
        if (_toolPointer is not null)
            FinishTool(commit: timestamp - _toolStart > PinchGrace && _toolMoved);

        var (center, spread) = Spread();
        _pinch = new PinchState(timestamp, center, spread, _scale, ToPage(center), _touches.Count);
    }

    private void MovePinch()
    {
        if (_pinch is not { } pinch || _touches.Count == 0)
            return;

        var (center, spread) = Spread();
        if (_touches.Count != pinch.Touches)
        {
            // A finger joined or left: carry on from here.
            _pinch = pinch with
            {
                StartCenter = center,
                StartSpread = spread,
                StartScale = _scale,
                Anchor = ToPage(center),
                Touches = _touches.Count,
                MaxTouches = Math.Max(pinch.MaxTouches, _touches.Count),
            };
            return;
        }

        // Fingers that barely move are a tap (undo or redo), and leave the view alone.
        var moved = Math.Max(pinch.Moved, Distance(center, pinch.StartCenter) + Math.Abs(spread - pinch.StartSpread));
        _pinch = pinch with { Moved = moved };
        if (moved < TapSlop)
            return;

        var scale = pinch.StartSpread > 20 && spread > 20
            ? Math.Clamp(pinch.StartScale * spread / pinch.StartSpread, MinScale, MaxScale)
            : _scale;
        var at = ToVirtual(center);
        _viewTicker.Stop();
        _scale = scale;
        _origin = new Point(at.X - (pinch.Anchor.X * scale), at.Y - (pinch.Anchor.Y * scale));
        SetFitted(false);
        InvalidateVisual();
    }

    private void EndPinch(ulong timestamp)
    {
        if (_pinch is not { } pinch)
            return;

        if (_touches.Count > 0)
        {
            MovePinch();
            return;
        }

        _pinch = null;
        if (timestamp - pinch.Started <= TapTime && pinch.Moved < TapSlop)
        {
            if (pinch.MaxTouches == 2)
                _studio?.Undo();
            else if (pinch.MaxTouches >= 3)
                _studio?.Redo();
        }
    }

    private (Point Center, double Spread) Spread()
    {
        var points = _touches.Values.ToArray();
        var center = new Point(points.Average(p => p.X), points.Average(p => p.Y));
        var spread = points.Length > 1 ? points.Average(p => Distance(p, center)) * 2 : 0;
        return (center, spread);
    }

    private void UpdateBand()
    {
        _hasBand = false;
        var state = Fold.GetState(this);
        if (state.FoldBounds is not { } fold || state.Posture == DevicePosture.Closed
            || state.Orientation != FoldOrientation.Vertical
            || TopLevel.GetTopLevel(this) is not { } topLevel
            || topLevel.TransformToVisual(this) is not { } toLocal)
        {
            return;
        }

        var local = fold.TransformToAABB(toLocal);
        if (local.X > 0 && local.Right < Bounds.Width && local.Y < Bounds.Height && local.Bottom > 0)
        {
            _hasBand = true;
            _bandStart = local.X;
            _bandWidth = local.Width;
        }
    }

    private Point ToVirtual(Point local)
    {
        if (!_hasBand || local.X < _bandStart)
            return local;

        return new Point(Math.Max(_bandStart, local.X - _bandWidth), local.Y);
    }

    private Point ToPage(Point local)
    {
        var at = ToVirtual(local);
        return new Point((at.X - _origin.X) / _scale, (at.Y - _origin.Y) / _scale);
    }

    // Where the page is drawn: once, or a half either side of the fold's band.
    private (SKRect Clip, SKMatrix Transform)[] Segments()
    {
        var page = SKMatrix.CreateScale((float)_scale, (float)_scale)
            .PostConcat(SKMatrix.CreateTranslation((float)_origin.X, (float)_origin.Y));
        var width = (float)Bounds.Width;
        var height = (float)Bounds.Height;
        if (!_hasBand)
            return [(new SKRect(0, 0, width, height), page)];

        var start = (float)_bandStart;
        var end = (float)(_bandStart + _bandWidth);
        return
        [
            (new SKRect(0, 0, start, height), page),
            (new SKRect(end, 0, width, height), page.PostConcat(SKMatrix.CreateTranslation((float)_bandWidth, 0))),
        ];
    }

    private void SetFitted(bool fitted)
    {
        if (_fitted == fitted)
            return;

        _fitted = fitted;
        FittedChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPaintChanged(object? sender, EventArgs e) => InvalidateVisual();

    private void OnDocumentOpened(object? sender, EventArgs e)
    {
        FinishTool(commit: false);
        StopPress();
        FitToView();
    }

    private void OnPressed(object? sender, PressedEventArgs e)
    {
        if (_studio is not { } studio)
            return;

        StopPress();
        _press = new PressReveal(e.Layers.ToDictionary(step => step.Layer, step => SKImage.FromBitmap(step.Before)), e.Fold, 0);
        _pressSync = studio.Document.Sync;
        _pressTicker.Start();
    }

    private void OnBrushAdjusted(object? sender, EventArgs e)
    {
        _showDab = true;
        _dabTimer.Stop();
        _dabTimer.Start();
        InvalidateVisual();
    }

    private (double Scale, Point Origin) FitTarget()
    {
        var document = _studio!.Document;
        var insets = FitInsets;
        var width = VirtualWidth - insets.Left - insets.Right - (2 * FitMargin);
        var height = Bounds.Height - insets.Top - insets.Bottom - (2 * FitMargin);
        var scale = Math.Max(0.01, Math.Min(width / document.Width, height / document.Height));
        return (scale, new Point(
            insets.Left + FitMargin + ((width - (document.Width * scale)) / 2),
            insets.Top + FitMargin + ((height - (document.Height * scale)) / 2)));
    }

    // Eases the view to fit, zooming about the page rather than sliding it.
    private void OnViewFrame(TimeSpan elapsed)
    {
        if (_studio is null)
        {
            _viewTicker.Stop();
            return;
        }

        var t = Math.Clamp(elapsed.TotalMilliseconds / 260, 0, 1);
        var eased = 1 - Math.Pow(1 - t, 3);
        var (scale, origin) = FitTarget();
        _scale = _viewFrom.Scale + ((scale - _viewFrom.Scale) * eased);
        _origin = new Point(
            _viewFrom.Origin.X + ((origin.X - _viewFrom.Origin.X) * eased),
            _viewFrom.Origin.Y + ((origin.Y - _viewFrom.Origin.Y) * eased));
        InvalidateVisual();
        if (t >= 1)
            _viewTicker.Stop();
    }

    // The print spreads out from the fold, quickly at first, as paper peels open.
    private void OnPressFrame(TimeSpan elapsed)
    {
        if (_press is not { } press)
            return;

        var t = Math.Clamp(elapsed.TotalMilliseconds / 900, 0, 1);
        var eased = 1 - Math.Pow(1 - t, 3);
        _press = press with { Reach = (float)eased };
        InvalidateVisual();
        if (t >= 1)
            StopPress();
    }

    private void StopPress()
    {
        _pressTicker.Stop();
        if (_press is not { } press)
            return;

        _press = null;

        // The render thread draws under the document's lock, and skips an image freed here. OnPressed sets
        // the lock with the press, so it's there whenever a press is.
        lock (_pressSync!)
        {
            foreach (var image in press.Before.Values)
                image.Dispose();
        }

        _pressSync = null;
        InvalidateVisual();
    }

    // A pen counts as in use until it has been lifted for a few seconds; after that, fingers draw again.
    private bool IsPenInUse(ulong now) => _penLiftedAt is { } lifted && now - lifted < PenIdleMilliseconds;

    private static double Distance(Point a, Point b) => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));

    private readonly record struct Dab(Point Center, double Radius, float Opacity, bool Eraser);

    private sealed record PinchState(
        ulong Started,
        Point StartCenter,
        double StartSpread,
        double StartScale,
        Point Anchor,
        int MaxTouches)
    {
        public int Touches { get; init; } = MaxTouches;

        public double Moved { get; init; }
    }

    /// <summary>
    /// Draws the page on the render thread, from a copy of what it needs.
    /// </summary>
    private sealed class PageDrawing(
        Rect bounds,
        Document document,
        LiveStroke? stroke,
        PressReveal? press,
        (SKRect Clip, SKMatrix Transform)[] segments,
        bool symmetry,
        (double Start, double Width)? band,
        Point? hover,
        double hoverRadius,
        Point? pick,
        Dab? dab,
        SKColor color,
        double scale) : ICustomDrawOperation
    {
        private static readonly SKColor Accent = new(0x7C, 0x6B, 0xFF);

        public Rect Bounds => bounds;

        // The whole canvas takes input, desk around the page included.
        public bool HitTest(Point p) => bounds.Contains(p);

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
            var page = SKRect.Create(document.Width, document.Height);
            var pixel = (float)(1 / scale);

            foreach (var (clip, transform) in segments)
            {
                canvas.Save();
                canvas.ClipRect(clip);
                canvas.Concat(in transform);

                // The page lies on the desk with a soft shadow under it.
                using (var blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 9 * pixel))
                using (var shadow = new SKPaint { Color = SKColors.Black.WithAlpha(110), MaskFilter = blur })
                {
                    canvas.DrawRect(SKRect.Create(0, 5 * pixel, document.Width, document.Height), shadow);
                }

                lock (document.Sync)
                {
                    if (!document.IsDisposed)
                    {
                        DocumentRenderer.DrawPaper(canvas, document);
                        DocumentRenderer.DrawLayers(canvas, document, stroke, press);
                    }
                }

                if (symmetry)
                {
                    using var dash = SKPathEffect.CreateDash([8 * pixel, 6 * pixel], 0);
                    using var axis = new SKPaint
                    {
                        Color = Accent.WithAlpha(170),
                        IsAntialias = true,
                        Style = SKPaintStyle.Stroke,
                        StrokeWidth = 1.5f * pixel,
                        PathEffect = dash,
                    };
                    canvas.DrawLine(document.Width / 2f, 0, document.Width / 2f, document.Height, axis);
                }

                canvas.Restore();
            }

            if (band is { } fold)
            {
                var spread = segments[0].Transform.MapRect(page);
                DrawGutter(canvas, spread, fold);

                // With the page's middle on the fold, the axis is the fold itself: mark its edges.
                var axis = segments[0].Transform.MapPoint(document.Width / 2f, 0).X;
                if (symmetry && Math.Abs(axis - fold.Start) < 2)
                    DrawFoldAxis(canvas, spread, fold);
            }

            if (dab is { } brush)
                DrawDab(canvas, brush);

            if (hover is { } at && hoverRadius > 1.5)
            {
                using var ring = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f };
                ring.Color = SKColors.Black.WithAlpha(120);
                canvas.DrawCircle((float)at.X, (float)at.Y, (float)hoverRadius + 1, ring);
                ring.Color = SKColors.White.WithAlpha(220);
                canvas.DrawCircle((float)at.X, (float)at.Y, (float)hoverRadius, ring);
            }

            if (pick is { } picking)
                DrawLoupe(canvas, picking);
        }

        // The pages curve down into the fold, as a book's do, shaded where they meet it.
        private void DrawGutter(SKCanvas canvas, SKRect left, (double Start, double Width) fold)
        {
            var start = (float)fold.Start;
            var end = (float)(fold.Start + fold.Width);
            var right = left;
            right.Offset((float)fold.Width, 0);
            const float reach = 34;

            using var shade = new SKPaint();
            if (left.Left < start && left.Right > start - reach)
            {
                using var gradient = SKShader.CreateLinearGradient(
                    new SKPoint(start - reach, 0), new SKPoint(start, 0),
                    [SKColors.Black.WithAlpha(0), SKColors.Black.WithAlpha(46)], SKShaderTileMode.Clamp);
                shade.Shader = gradient;
                canvas.DrawRect(SKRect.Intersect(left, new SKRect(start - reach, left.Top, start, left.Bottom)), shade);
            }

            if (right.Right > end && right.Left < end + reach)
            {
                using var gradient = SKShader.CreateLinearGradient(
                    new SKPoint(end, 0), new SKPoint(end + reach, 0),
                    [SKColors.Black.WithAlpha(46), SKColors.Black.WithAlpha(0)], SKShaderTileMode.Clamp);
                shade.Shader = gradient;
                canvas.DrawRect(SKRect.Intersect(right, new SKRect(end, right.Top, end + reach, right.Bottom)), shade);
            }

            shade.Shader = null;
        }

        private static void DrawFoldAxis(SKCanvas canvas, SKRect spread, (double Start, double Width) fold)
        {
            using var dash = SKPathEffect.CreateDash([8, 6], 0);
            using var axis = new SKPaint
            {
                Color = Accent.WithAlpha(200),
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2,
                PathEffect = dash,
            };
            var left = (float)fold.Start - 1;
            var right = (float)(fold.Start + fold.Width) + 1;
            canvas.DrawLine(left, spread.Top, left, spread.Bottom, axis);
            canvas.DrawLine(right, spread.Top, right, spread.Bottom, axis);
        }

        // The brush at its size and opacity, ringed so it shows on any paint.
        private void DrawDab(SKCanvas canvas, Dab brush)
        {
            var center = new SKPoint((float)brush.Center.X, (float)brush.Center.Y);
            var radius = (float)brush.Radius;
            using var paint = new SKPaint { IsAntialias = true };
            if (!brush.Eraser)
            {
                paint.Color = color.WithAlpha((byte)Math.Round(brush.Opacity * 255));
                canvas.DrawCircle(center, radius, paint);
            }

            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = 1.5f;
            paint.Color = SKColors.Black.WithAlpha(110);
            canvas.DrawCircle(center, radius + 1.5f, paint);
            paint.Color = SKColors.White.WithAlpha(230);
            canvas.DrawCircle(center, radius, paint);
        }

        // The color under the finger, shown above it where the finger doesn't hide it.
        private void DrawLoupe(SKCanvas canvas, Point at)
        {
            var center = new SKPoint((float)at.X, (float)at.Y - 64);
            using var paint = new SKPaint { IsAntialias = true };
            paint.Color = SKColors.Black.WithAlpha(90);
            canvas.DrawCircle(center.X, center.Y + 2, 31, paint);
            paint.Color = SKColors.White;
            canvas.DrawCircle(center, 30, paint);
            paint.Color = color;
            canvas.DrawCircle(center, 25, paint);
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = 2;
            paint.Color = SKColors.White;
            canvas.DrawCircle((float)at.X, (float)at.Y, 6, paint);
        }
    }
}
