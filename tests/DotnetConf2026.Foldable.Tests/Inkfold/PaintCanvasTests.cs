using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Inkfold;
using Inkfold.Controls;
using Inkfold.Painting;
using Xunit;

namespace DotnetConf2026.Foldable.Tests.Inkfold;

public sealed class PaintCanvasTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"inkfold-canvas-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    [AvaloniaFact]
    public void A_palm_landing_while_the_pen_draws_leaves_the_line_whole()
    {
        var withPalm = DrawLine(palm: true);
        var without = DrawLine(palm: false);

        Assert.True(without.Width > 0, "the pen painted");
        Assert.Equal(without, withPalm);
    }

    [AvaloniaFact]
    public void A_thumb_on_the_page_while_the_press_prints_leaves_the_print_alone()
    {
        var studio = new Studio(new Gallery(Path.Combine(_root, "press")));
        var canvas = new PaintCanvas { Studio = studio };
        var window = new Window { Width = 951, Height = 669, Content = canvas };
        window.Show();
        Pump();
        var input = new Pointers(window, canvas);
        var finger = new Pointer(1, PointerType.Touch, true);
        input.Down(finger, new Point(300, 300));
        input.Move(finger, new Point(400, 320));
        input.Up(finger, new Point(400, 320));

        var pressed = false;
        string? notice = null;
        studio.Pressed += (_, _) => pressed = true;
        studio.Notice += (_, text) => notice = text;

        // The device opens and the press starts. A thumb taps the page, and a two-finger tap asks to undo.
        var press = studio.PressAsync(PressFold.Vertical);
        input.Down(finger, new Point(600, 400));
        input.Up(finger, new Point(600, 400));
        studio.Undo();
        while (!press.IsCompleted)
        {
            Dispatcher.UIThread.RunJobs();
            System.Threading.Thread.Sleep(10);
        }

        Pump();
        Assert.Null(notice);
        Assert.True(pressed);
        window.Close();
    }

    [AvaloniaFact]
    public void A_thumb_that_lands_before_the_press_and_lifts_during_it_leaves_the_print_alone()
    {
        var studio = new Studio(new Gallery(Path.Combine(_root, "early")));
        var canvas = new PaintCanvas { Studio = studio };
        var window = new Window { Width = 951, Height = 669, Content = canvas };
        window.Show();
        Pump();
        var input = new Pointers(window, canvas);
        var finger = new Pointer(1, PointerType.Touch, true);
        input.Down(finger, new Point(300, 300));
        input.Move(finger, new Point(400, 320));
        input.Up(finger, new Point(400, 320));

        var pressed = false;
        string? notice = null;
        studio.Pressed += (_, _) => pressed = true;
        studio.Notice += (_, text) => notice = text;

        // The thumb touches the inner screen while the device is still opening, then the press starts,
        // then the thumb lifts.
        input.Down(finger, new Point(600, 400));
        input.Move(finger, new Point(640, 420));
        var press = studio.PressAsync(PressFold.Vertical);
        input.Up(finger, new Point(640, 420));
        while (!press.IsCompleted)
        {
            Dispatcher.UIThread.RunJobs();
            System.Threading.Thread.Sleep(10);
        }

        Pump();
        Assert.Null(notice);
        Assert.True(pressed);
        window.Close();
    }

    [AvaloniaFact]
    public void Undo_and_redo_are_off_while_the_press_prints()
    {
        var studio = new Studio(new Gallery(Path.Combine(_root, "buttons")));
        var rail = new ActionRail { Studio = studio };
        var window = new Window { Width = 300, Height = 600, Content = rail };
        window.Show();
        studio.AddLayer();
        Pump();
        var undo = rail.FindControl<Button>("UndoButton")!;
        Assert.True(undo.IsEnabled);

        var press = studio.PressAsync(PressFold.Vertical);
        Assert.False(undo.IsEnabled);
        while (!press.IsCompleted)
        {
            Dispatcher.UIThread.RunJobs();
            System.Threading.Thread.Sleep(10);
        }

        Pump();
        Assert.True(undo.IsEnabled);
        window.Close();
    }

    private static void Pump()
    {
        for (var i = 0; i < 3; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }

    // A pen line from left to right, optionally with a palm landing halfway and moving a little.
    // Returns the painted area, in page pixels.
    private PixelRect DrawLine(bool palm)
    {
        var studio = new Studio(new Gallery(Path.Combine(_root, palm ? "palm" : "pen")));
        var canvas = new PaintCanvas { Studio = studio };
        var window = new Window { Width = 951, Height = 669, Content = canvas };
        window.Show();
        for (var i = 0; i < 3; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }

        var input = new Pointers(window, canvas);
        var pen = new Pointer(1, PointerType.Pen, true);
        var hand = new Pointer(2, PointerType.Touch, false);
        input.Down(pen, new Point(300, 300));
        for (var x = 320; x <= 500; x += 20)
        {
            input.Move(pen, new Point(x, 300));
            if (palm && x == 400)
            {
                input.Down(hand, new Point(600, 520));
                input.Move(hand, new Point(612, 528));
            }
        }

        input.Up(pen, new Point(500, 300));
        if (palm)
            input.Up(hand, new Point(612, 528));

        var pixels = studio.Document.Active.Pixels;
        int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
        for (var y = 0; y < pixels.Height; y++)
        {
            for (var x = 0; x < pixels.Width; x++)
            {
                if (pixels.GetPixel(x, y).Alpha == 0)
                    continue;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
        }

        window.Close();
        return right < 0 ? default : new PixelRect(left, top, right - left + 1, bottom - top + 1);
    }
}
