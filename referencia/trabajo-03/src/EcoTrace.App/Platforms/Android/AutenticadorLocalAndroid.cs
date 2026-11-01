using Android.Hardware.Biometrics;
using Android.OS;
using EcoTrace.Mobile.Core.Sesion;

namespace EcoTrace.App.Servicios;

/// <summary>
/// Abre el cuadro del sistema para identificarse con la huella, el rostro o, si el teléfono (o el emulador) no tiene biometría,
/// con el PIN, el patrón o la contraseña del dispositivo. Es lo que desbloquea la sesión degradada sin conexión (ADR 0005).
/// Pide Android 11 (API 30) o superior, que es desde donde el sistema permite «biometría o credencial del dispositivo».
/// </summary>
public sealed class AutenticadorLocalAndroid : IAutenticadorLocal
{
    // BiometricManager.Authenticators.BIOMETRIC_STRONG (0x000F) | DEVICE_CREDENTIAL (0x8000): el enlace de .NET no expone las constantes.
    private const int BiometriaFuerteOCredencial = 0x000F | 0x8000;

    public Task<bool> VerificarAsync(string motivo, CancellationToken ct = default)
    {
        var actividad = Platform.CurrentActivity;
        if (actividad is null || !OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            return Task.FromResult(false);
        }

        var resultado = new TaskCompletionSource<bool>();
        var senal = new CancellationSignal();
        ct.Register(() =>
        {
            senal.Cancel();
            resultado.TrySetResult(false);
        });

        MainThread.BeginInvokeOnMainThread(() =>
        {
            var cuadro = new BiometricPrompt.Builder(actividad)
                .SetTitle("EcoTrace")
                .SetDescription(motivo)
                .SetAllowedAuthenticators(BiometriaFuerteOCredencial)
                .Build();

            cuadro.Authenticate(senal, actividad.MainExecutor!, new Respuesta(resultado));
        });

        return resultado.Task;
    }

    private sealed class Respuesta(TaskCompletionSource<bool> resultado) : BiometricPrompt.AuthenticationCallback
    {
        public override void OnAuthenticationSucceeded(BiometricPrompt.AuthenticationResult? result) => resultado.TrySetResult(true);

        public override void OnAuthenticationError(BiometricErrorCode errorCode, Java.Lang.ICharSequence? errString) =>
            resultado.TrySetResult(false);
    }
}
