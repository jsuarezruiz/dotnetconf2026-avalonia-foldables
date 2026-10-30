using Android.App;
using Android.Content.PM;
using Avalonia.Android;

namespace BotVsBugs.Android;

// Folding and unfolding changes the screen; handling the change here keeps the activity, and the
// level in progress, alive across it.
[Activity(
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ResizeableActivity = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode
        | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : AvaloniaMainActivity
{
}
