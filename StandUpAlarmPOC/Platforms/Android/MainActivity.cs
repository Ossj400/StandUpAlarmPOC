using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;

namespace StandUpAlarmPOC;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        ActivityCompat.RequestPermissions(
    this,
    new[] { Android.Manifest.Permission.Camera },
    0);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            var channel = new NotificationChannel("vibration_channel",
                "Vibration Channel", NotificationImportance.High);
            ((NotificationManager)GetSystemService(NotificationService))
                .CreateNotificationChannel(channel);
        }
    }
}
