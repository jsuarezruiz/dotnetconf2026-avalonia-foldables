using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using BotVsBugs.Controls;
using Xunit;

namespace DotnetConf2026.Foldable.Tests.BotVsBugs;

public class HingeGaugeTests
{
    [AvaloniaFact]
    public void Armed_the_wedge_shows_the_held_pull_not_the_live_angle()
    {
        // Folded to 100°, then opened to 170° on the way to firing: the pull is held at 80%.
        var gauge = new HingeGauge { Angle = 170, Pull = 0.8, IsArmed = true };
        var window = new Window { Width = 300, Height = 200, Content = gauge };
        window.Show();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame()!;
        using var pixels = frame.Lock();

        // The gauge's own geometry: a pivot near the bottom left, and the wedge at 0.7 of the arm.
        var length = Math.Min(300 * 0.42, 200 * 0.62);
        var pivot = new Point(300 * 0.38, 200 * 0.78);
        int At(double degrees)
        {
            var radians = degrees * Math.PI / 180;
            var x = (int)(pivot.X + (Math.Cos(radians) * length * 0.5));
            var y = (int)(pivot.Y - (Math.Sin(radians) * length * 0.5));
            return Marshal.ReadInt32(pixels.Address, (y * pixels.RowBytes) + (x * 4));
        }

        // 120° is inside the held pull (172° down to 100°); 30° is outside any wedge.
        Assert.NotEqual(At(30), At(120));
        window.Close();
    }
}
