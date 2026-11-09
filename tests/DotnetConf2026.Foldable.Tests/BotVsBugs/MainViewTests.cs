using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BotVsBugs.Views;
using Xunit;

namespace DotnetConf2026.Foldable.Tests.BotVsBugs;

public class MainViewTests
{
    [AvaloniaFact]
    public void Escape_pauses_a_level_started_from_the_menu()
    {
        // The tests run in the Inbox app, so the game's own brushes come in for this one.
        var brushes = new ResourceInclude(new Uri("avares://BotVsBugs"))
        {
            Source = new Uri("avares://BotVsBugs/Styles/Resources.axaml"),
        };
        var resources = Application.Current!.Resources.MergedDictionaries;
        resources.Add(brushes);
        try
        {
            var view = new MainView();
            var window = new Window { Width = 951, Height = 669, Content = view };
            window.Show();
            Pump();

            // Play takes the focus when it's clicked, then leaves with the menu.
            var menu = view.FindControl<Panel>("MenuLayer")!;
            var play = menu.GetVisualDescendants().OfType<Button>()
                .First(button => button.Content is string text && text.StartsWith("Play level", StringComparison.Ordinal));
            var center = play.TranslatePoint(new Point(play.Bounds.Width / 2, play.Bounds.Height / 2), window)!.Value;
            window.MouseDown(center, MouseButton.Left);
            window.MouseUp(center, MouseButton.Left);
            Pump();
            Assert.False(menu.IsVisible);

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Pump();
            Assert.True(menu.IsVisible);
            window.Close();
        }
        finally
        {
            resources.Remove(brushes);
        }
    }

    private static void Pump()
    {
        for (var i = 0; i < 3; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
