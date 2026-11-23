using Android.App;
using Android.Content.PM;
using Android.OS;

namespace Repro;

[Activity(Name = "com.example.shellicon.MainActivity", Theme = "@style/Maui.SplashTheme", MainLauncher = true, Exported = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // The CI script launches one scenario per process: `am start --es case <name>`
        Scenario.CaseName = Intent?.GetStringExtra("case") ?? "control";
        base.OnCreate(savedInstanceState);
    }
}
