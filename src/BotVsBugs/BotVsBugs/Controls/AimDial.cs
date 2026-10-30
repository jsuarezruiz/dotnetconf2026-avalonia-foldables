using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using BotVsBugs.Game;

namespace BotVsBugs.Controls;

/// <summary>
/// Sets the aim for hinge shots: drag around the dial to point the bot.
/// </summary>
public sealed class AimDial : Control
{
    public static readonly StyledProperty<double> AimProperty =
        AvaloniaProperty.Register<AimDial, double>(nameof(Aim), GameView.DefaultAim);

    private static readonly IBrush TrackBrush = new SolidColorBrush(Color.Parse("#4A3A80"));
    private static readonly IBrush NeedleBrush = new SolidColorBrush(Color.Parse("#FFE45C"));

    private bool _isDragging;

    static AimDial()
    {
        AffectsRender<AimDial>(AimProperty);
    }

    public AimDial()
    {
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
    }

    /// <summary>
    /// Gets or sets the aim in radians, counterclockwise from pointing right.
    /// </summary>
    public double Aim
    {
        get => GetValue(AimProperty);
        set => SetValue(AimProperty, Math.Clamp(value, GameView.MinAim, GameView.MaxAim));
    }

    /// <summary>
    /// Occurs when a drag changes the aim.
    /// </summary>
    public event EventHandler? AimChanged;

    // The dial turns around a point on the left, like the sling, sized so the whole track, from aiming
    // 34° down to 83° up, fits inside the control.
    private Point Pivot => new(Bounds.Width * 0.22d, Bounds.Height * 0.6d);

    private double Radius => Math.Max(10d, Math.Min(Bounds.Width * 0.7d, Bounds.Height * 0.56d));

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _isDragging = true;
        e.Pointer.Capture(this);
        e.Handled = true;
        Point(e.GetPosition(this));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_isDragging)
            Point(e.GetPosition(this));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _isDragging = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _isDragging = false;
    }

    private void Point(Point position)
    {
        var offset = position - Pivot;
        if (Math.Abs(offset.X) + Math.Abs(offset.Y) < 4d)
            return;

        Aim = Math.Atan2(-offset.Y, offset.X);
        AimChanged?.Invoke(this, EventArgs.Empty);
    }

    public override void Render(DrawingContext context)
    {
        if (Bounds.Width < 40d || Bounds.Height < 40d)
            return;

        var pivot = Pivot;
        var radius = Radius;

        // The track covers the aims the game allows.
        var track = new StreamGeometry();
        using (var g = track.Open())
        {
            g.BeginFigure(pivot + Polar(GameView.MinAim, radius), false);
            g.ArcTo(pivot + Polar(GameView.MaxAim, radius), new Size(radius, radius), 0d, false, SweepDirection.CounterClockwise);
        }

        context.DrawGeometry(null, new Pen(TrackBrush, 10d, lineCap: PenLineCap.Round), track);

        var tip = pivot + Polar(Aim, radius);
        context.DrawLine(new Pen(NeedleBrush, 4d, lineCap: PenLineCap.Round), pivot, tip);
        context.DrawEllipse(NeedleBrush, null, tip, 9d, 9d);

        // The bot at the pivot, ready to go where the needle points.
        var bot = Sprites.Get("Bot/bot_head.png");
        var width = Math.Min(44d, radius * 0.45d);
        var height = width * 142d / 121d;
        context.DrawImage(bot, new Rect(pivot.X - (width / 2d), pivot.Y - (height * 81.5d / 142d), width, height));
    }

    private static Point Polar(double angle, double radius) => new(Math.Cos(angle) * radius, -Math.Sin(angle) * radius);
}
