using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(DotnetConf2026.Foldable.Tests.TestAppBuilder))]

namespace DotnetConf2026.Foldable.Tests;

// The headless tests run with the Inbox app's own theme and styles, drawn with Skia so text measures and
// wraps as it does on a device.
public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<global::Inbox.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
