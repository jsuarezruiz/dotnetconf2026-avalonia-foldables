using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using BotVsBugs.Views;

namespace BotVsBugs;

public partial class App : Application
{
    /// <summary>
    /// Gets or sets the factory the desktop head uses to host the game in its fold simulator.
    /// </summary>
    public static Func<MainView, Window>? MainWindowFactory { get; set; }

    public override void Initialize()
    {
        // The app is in American English, so its dates, times and numbers are too, whatever the machine's
        // region: English with a Spanish region, say, would otherwise show 24-hour times and "12.345".
        var american = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = american;
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = american;
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var view = new MainView();
            desktop.MainWindow = MainWindowFactory?.Invoke(view)
                ?? new Window { Title = "Bot vs Bugs", Width = 951, Height = 669, Content = view };
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime activity)
        {
            activity.MainViewFactory = () => new MainView();
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            singleView.MainView = new MainView();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
