namespace EcoTrace.App.Modulos.Identity.Logica;

/// <summary>En qué estado está la sesión del conductor (ADR 0005).</summary>
public enum ModoDeSesion
{
    /// <summary>Nunca inició sesión en este teléfono, o la cerró.</summary>
    SinSesion = 0,

    /// <summary>Hay un access token vigente.</summary>
    Activa = 1,

    /// <summary>
    /// El access token venció y no se pudo renovar (sin señal o sin respuesta de Identity), dentro de las 48 horas desde el
    /// último contacto real con Identity. Se puede seguir trabajando tras desbloquear con huella o PIN, pero sin acciones
    /// que muevan dinero ni cierren un ciclo de negocio crítico.
    /// </summary>
    Degradada = 2,

    /// <summary>Pasaron más de 48 horas sin contacto con Identity: hay que conectarse e iniciar sesión.</summary>
    Expirada = 3
}

public enum ResultadoDesbloqueo
{
    Desbloqueada = 1,

    /// <summary>La persona cerró el cuadro o no pasó la verificación.</summary>
    Cancelada = 2,

    /// <summary>El teléfono no tiene bloqueo de pantalla (PIN, patrón o huella) o su Android no permite el cuadro.</summary>
    NoDisponible = 3,

    /// <summary>La sesión no está degradada: no hay nada que desbloquear.</summary>
    NoAplica = 4
}

/// <summary>
/// Pide a la persona que se identifique en el teléfono: huella, rostro o, si no hay biometría (por ejemplo en el
/// emulador), el PIN o el patrón del dispositivo. En Android es el cuadro del sistema (<c>BiometricPrompt</c>).
/// </summary>
public interface IAutenticadorLocal
{
    Task<ResultadoDesbloqueo> VerificarAsync(string motivo, CancellationToken ct = default);
}

/// <summary>
/// La sesión no permite encolar esa acción ahora. Hereda de <see cref="InvalidOperationException"/> para que los demás
/// módulos la atrapen sin conocer el módulo Identity; su mensaje ya está escrito para mostrárselo al conductor.
/// </summary>
public sealed class AccionNoPermitidaException(string mensaje) : InvalidOperationException(mensaje);

/// <summary>Las reglas de la sesión sin conexión del ADR 0005. Funciones puras: se prueban sin teléfono.</summary>
public static class PoliticaDeSesion
{
    /// <summary>Cuánto dura como máximo una sesión degradada, contado desde el último contacto real con Identity.</summary>
    public static readonly TimeSpan LimiteDegradada = TimeSpan.FromHours(48);

    /// <summary>
    /// Acciones que mueven dinero o cierran un ciclo de negocio crítico: con la sesión degradada no se encolan. Confirmar una
    /// entrega dispara el Saga que libera el pago. Si un módulo agrega otra acción crítica, se suma aquí.
    /// </summary>
    public static readonly IReadOnlySet<(string Modulo, string Tipo)> AccionesCriticas =
        new HashSet<(string, string)> { ("Cargo", "ConfirmarEntrega") };

    public static bool EsCritica(string modulo, string tipo) => AccionesCriticas.Contains((modulo, tipo));

    public static ModoDeSesion Evaluar(DatosDeSesion? sesion, DateTime ahoraUtc)
    {
        if (sesion is null)
        {
            return ModoDeSesion.SinSesion;
        }

        if (sesion.AccesoExpiraEnUtc > ahoraUtc)
        {
            return ModoDeSesion.Activa;
        }

        return ahoraUtc - sesion.UltimoContactoUtc <= LimiteDegradada ? ModoDeSesion.Degradada : ModoDeSesion.Expirada;
    }

    /// <summary>Por qué no se puede encolar, o null si sí se puede.</summary>
    public static string? Impedimento(ModoDeSesion modo, bool desbloqueada, bool critica) => modo switch
    {
        ModoDeSesion.Activa => null,
        ModoDeSesion.Degradada when critica =>
            "Sin conexión y con la sesión vencida no se pueden confirmar entregas ni mover dinero. Se habilita al reconectar.",
        ModoDeSesion.Degradada when !desbloqueada =>
            "Estás sin conexión y tu sesión venció. Abre la pestaña Perfil y desbloquea con tu huella o PIN para seguir.",
        ModoDeSesion.Degradada => null,
        ModoDeSesion.Expirada =>
            "Pasaron más de 48 horas sin conexión con Identity. Conéctate e inicia sesión de nuevo para seguir.",
        _ => "Inicia sesión en la pestaña Perfil para continuar."
    };
}
