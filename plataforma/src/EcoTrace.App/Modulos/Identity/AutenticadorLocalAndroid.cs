using Android.App;
using Android.Content;
using Android.Hardware.Biometrics;
using Android.OS;
using EcoTrace.App.Modulos.Identity.Logica;

// Permiso para el cuadro de huella o PIN. Se declara aquí y no en el AndroidManifest compartido: el build lo agrega solo.
[assembly: UsesPermission(Android.Manifest.Permission.UseBiometric)]

namespace EcoTrace.App.Modulos.Identity;

/// <summary>
/// Abre el cuadro del sistema para identificarse con la huella, el rostro o, si el teléfono (o el emulador) no tiene
/// biometría, con el PIN, el patrón o la contraseña del dispositivo. Es lo que desbloquea la sesión degradada (ADR 0005).
/// Pide Android 10 (API 29) o superior; antes de eso el sistema no ofrece este cuadro con el PIN como alternativa.
/// </summary>
public sealed class AutenticadorLocalAndroid : IAutenticadorLocal
{
    // BiometricManager.Authenticators: BIOMETRIC_STRONG (0x000F) | DEVICE_CREDENTIAL (0x8000).
    private const int BiometriaOCredencialDelDispositivo = 0x000F | 0x8000;

    public Task<ResultadoDesbloqueo> VerificarAsync(string motivo, CancellationToken ct = default) =>
        MainThread.InvokeOnMainThreadAsync(() => MostrarAsync(motivo, ct));

    private static Task<ResultadoDesbloqueo> MostrarAsync(string motivo, CancellationToken ct)
    {
        var actividad = Platform.CurrentActivity;
        if (actividad is null || !OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            return Task.FromResult(ResultadoDesbloqueo.NoDisponible);
        }

        // Sin bloqueo de pantalla no hay con qué verificar: el cuadro fallaría.
        if (actividad.GetSystemService(Context.KeyguardService) is not KeyguardManager { IsDeviceSecure: true })
        {
            return Task.FromResult(ResultadoDesbloqueo.NoDisponible);
        }

        var resultado = new TaskCompletionSource<ResultadoDesbloqueo>(TaskCreationOptions.RunContinuationsAsynchronously);
        var constructor = new BiometricPrompt.Builder(actividad)
            .SetTitle("EcoTrace")!
            .SetSubtitle(motivo)!;

        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            constructor.SetAllowedAuthenticators(BiometriaOCredencialDelDispositivo);
        }
        else
        {
#pragma warning disable CA1422 // En Android 10 es la única forma de ofrecer el PIN como alternativa.
            constructor.SetDeviceCredentialAllowed(true);
#pragma warning restore CA1422
        }

        var cancelar = new CancellationSignal();
        ct.Register(cancelar.Cancel);
        constructor.Build().Authenticate(cancelar, actividad.MainExecutor!, new Respuesta(resultado));
        return resultado.Task;
    }

    private sealed class Respuesta(TaskCompletionSource<ResultadoDesbloqueo> resultado) : BiometricPrompt.AuthenticationCallback
    {
        public override void OnAuthenticationSucceeded(BiometricPrompt.AuthenticationResult? result) =>
            resultado.TrySetResult(ResultadoDesbloqueo.Desbloqueada);

        // Un intento fallido deja el cuadro abierto para reintentar; solo el error final cierra la verificación.
        public override void OnAuthenticationError(BiometricErrorCode errorCode, Java.Lang.ICharSequence? errString) =>
            resultado.TrySetResult(errorCode is BiometricErrorCode.Canceled or BiometricErrorCode.UserCanceled
                ? ResultadoDesbloqueo.Cancelada
                : ResultadoDesbloqueo.NoDisponible);
    }
}
