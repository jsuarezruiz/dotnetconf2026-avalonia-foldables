using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace BotVsBugs.Controls;

/// <summary>
/// The device from the side, as it stands in tabletop: one half on the table, the other at the hinge
/// angle, with the pull the fold puts on the sling.
/// </summary>
public sealed class HingeGauge : Control
{
    public static readonly StyledProperty<double?> AngleProperty =
        AvaloniaProperty.Register<HingeGauge, double?>(nameof(Angle));

    public static readonly StyledProperty<double> PullProperty =
        AvaloniaProperty.Register<HingeGauge, double>(nameof(Pull));

    public static readonly StyledProperty<bool> IsArmedProperty =
        AvaloniaProperty.Register<HingeGauge, bool>(nameof(IsArmed));

    private static readonly Typeface GameTypeface = new(new FontFamily("avares://BotVsBugs/Assets/Fonts#Lilita One"));
    private static readonly IBrush DeviceBrush = new SolidColorBrush(Color.Parse("#E8E3FF"));
    private static readonly IBrush TableBrush = new SolidColorBrush(Color.Parse("#4A3A80"));
    private static readonly IBrush ArmedBrush = new SolidColorBrush(Color.Parse("#FFE45C"));
    private static readonly IBrush IdleBrush = new SolidColorBrush(Color.Parse("#9B83FF"));
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.Parse("#B8AEDB"));

    static HingeGauge()
    {
        AffectsRender<HingeGauge>(AngleProperty, PullProperty, IsArmedProperty);
    }

    /// <summary>
    /// Gets or sets the hinge angle in degrees, or null when the device reports none.
    /// </summary>
    public double? Angle
    {
        get => GetValue(AngleProperty);
        set => SetValue(AngleProperty, value);
    }

    public double Pull
    {
        get => GetValue(PullProperty);
        set => SetValue(PullProperty, value);
    }

    /// <summary>
    /// Gets or sets whether a fold has armed the shot, so opening fires it.
    /// </summary>
    public bool IsArmed
    {
        get => GetValue(IsArmedProperty);
        set => SetValue(IsArmedProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        if (size.Width < 40d || size.Height < 40d)
            return;

        var length = Math.Min(size.Width * 0.42d, size.Height * 0.62d);
        var pivot = new Point(size.Width * 0.38d, size.Height * 0.78d);
        var accent = IsArmed ? ArmedBrush : IdleBrush;

        context.DrawLine(new Pen(TableBrush, 3d, lineCap: PenLineCap.Round), new Point(4d, pivot.Y + 5d), new Point(size.Width - 4d, pivot.Y + 5d));

        if (Angle is not { } angle)
        {
            context.DrawLine(new Pen(DeviceBrush, 6d, lineCap: PenLineCap.Round), pivot, pivot + new Point(length, 0d));
            DrawText(context, "No hinge", 15d, MutedBrush, new Point(size.Width / 2d, size.Height * 0.3d));
            return;
        }

        // The pulled-back sweep, from where the band rests to how far it's drawn: once armed, that's the
        // deepest fold, held until the shot, while the device line follows the hinge.
        var radians = angle * Math.PI / 180d;
        var top = pivot + new Point(Math.Cos(radians) * length, -Math.Sin(radians) * length);
        if (Pull > 0d)
        {
            var rest = 172d * Math.PI / 180d;
            var drawn = (172d - (Math.Min(Pull, 1d) * 90d)) * Math.PI / 180d;
            var radius = length * 0.7d;
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(pivot, true);
                g.LineTo(pivot + new Point(Math.Cos(rest) * radius, -Math.Sin(rest) * radius));
                g.ArcTo(
                    pivot + new Point(Math.Cos(drawn) * radius, -Math.Sin(drawn) * radius),
                    new Size(radius, radius),
                    0d,
                    false,
                    SweepDirection.Clockwise);
                g.EndFigure(true);
            }

            using (context.PushOpacity(IsArmed ? 0.55d : 0.3d))
                context.DrawGeometry(accent, null, geometry);
        }

        var devicePen = new Pen(DeviceBrush, 6d, lineCap: PenLineCap.Round);
        context.DrawLine(devicePen, pivot, pivot + new Point(length, 0d));
        context.DrawLine(devicePen, pivot, top);
        context.DrawEllipse(accent, null, pivot, 5d, 5d);

        var labelX = size.Width * 0.8d;
        DrawText(context, $"{angle:0}°", 26d, Brushes.White, new Point(labelX, size.Height * 0.32d));
        DrawText(context, $"POWER {Pull * 100d:0}%", 13d, accent, new Point(labelX, size.Height * 0.56d));
    }

    private static void DrawText(DrawingContext context, string text, double size, IBrush brush, Point center)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, GameTypeface, size, brush);
        context.DrawText(formatted, center - new Point(formatted.Width / 2d, formatted.Height / 2d));
    }
}
