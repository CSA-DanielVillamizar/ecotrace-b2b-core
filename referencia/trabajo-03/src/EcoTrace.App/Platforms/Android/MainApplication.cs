using Android.App;
using Android.Runtime;

namespace EcoTrace.App;

// La plataforma de desarrollo y la de "produccion simulada" corren en http en el computador del desarrollador
// (10.0.2.2 desde el emulador). La configuracion de red solo permite ese host en claro; cualquier otro va por https.
[Application(NetworkSecurityConfig = "@xml/network_security_config")]
public class MainApplication : MauiApplication
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
