using Android.App;
using Android.Content.PM;
using Android.OS;

namespace EcoTrace.App;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode
        | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // La barra de estado toma el color de la franja de arriba de las pantallas, para que se vea continua.
#pragma warning disable CA1422
        Window?.SetStatusBarColor(Android.Graphics.Color.ParseColor("#0A2C2C"));
#pragma warning restore CA1422

        // Fondo oscuro: los iconos de la barra de estado (hora, bateria) tienen que ser claros.
        if (Window?.DecorView is { } vista)
        {
            AndroidX.Core.View.WindowCompat.GetInsetsController(Window, vista).AppearanceLightStatusBars = false;
        }
    }
}
