using System;
using nkast.Aether.Physics2D.Common;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using BotVsBugs.Controls;
using BotVsBugs.Game;
using Xunit;

namespace DotnetConf2026.Foldable.Tests.BotVsBugs;

public class GameViewTests
{
    [AvaloniaFact]
    public void Only_the_finger_on_the_bot_aims_and_fires()
    {
        var world = new GameWorld(Levels.All[0], 0);
        var game = new GameView { World = world };
        var window = new Window { Width = 951, Height = 669, Content = game };
        window.Show();
        for (var i = 0; i < 3; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }

        // The view's camera: the world fitted to the view, standing on the ground line.
        var scale = Math.Min(951 / GameWorld.ViewWidth, 669 / (GameWorld.ViewTop + GameWorld.GroundDepth));
        var origin = new Point((951 - (GameWorld.ViewWidth * scale)) / 2, 669 - (GameWorld.GroundDepth * scale));
        Point Screen(Vector2 p) => new(origin.X + (p.X * scale), origin.Y - (p.Y * scale));

        var input = new Pointers(window, game);
        var finger = new Pointer(1, PointerType.Touch, true);
        var thumb = new Pointer(2, PointerType.Touch, false);

        // A finger on the bot, then a thumb resting on the far side of the screen.
        input.Down(finger, Screen(GameWorld.SlingAnchor));
        var aim = game.AimAngle;
        input.Down(thumb, new Point(800, 300));
        input.Move(thumb, new Point(840, 340));
        Assert.Equal(aim, game.AimAngle);

        // Lifting the thumb doesn't fire.
        input.Up(thumb, new Point(840, 340));
        Assert.Null(world.Shot);

        // The finger pulls back and lets go: that fires.
        input.Move(finger, Screen(GameWorld.SlingAnchor - new Vector2(1.4f, 0.6f)));
        Assert.NotEqual(aim, game.AimAngle);
        input.Up(finger, Screen(GameWorld.SlingAnchor - new Vector2(1.4f, 0.6f)));
        Assert.NotNull(world.Shot);
    }
}
