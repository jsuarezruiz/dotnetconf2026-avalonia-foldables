using Android.App;
using Android.Content.PM;
using Avalonia.Android;

namespace Inbox.Android;

// Folding and unfolding changes the screen; handling the change here keeps the activity, and the
// open message and the reply in progress, alive across it.
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
