using System;
using Avalonia;
using Nova.Avalonia.Foldable.Simulator;

namespace Inkfold.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        App.MainWindowFactory = view => new FoldableSimulatorWindow(view, "Inkfold");
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
