using EcoTrace.Mobile.Core.Cola;

namespace EcoTrace.App.Modulos.Cargo;

/// <summary>
/// Una carga lista para mostrar: lo que dice el servidor, lo que el conductor ya hizo y todavía no se envió, y qué
/// cambios se le pueden ofrecer ahora.
/// </summary>
/// <param name="EstadoServidor">El estado que el servidor tenía la última vez que hubo conexión.</param>
/// <param name="EstadoMostrado">
/// El estado que se le muestra al conductor: el del servidor, salvo que haya una acción suya en cola (o recién
/// sincronizada) que lo cambie. La pantalla se actualiza de inmediato, sin esperar al servidor (ADR 0005).
/// </param>
/// <param name="PendienteDeSync">True si el estado mostrado viene de una acción que todavía no llegó al servidor.</param>
/// <param name="ConflictoRechazo">Tipo de conflicto (A, B o C) de la última acción de esta carga, si el servidor la rechazó.</param>
/// <param name="MotivoRechazo">Por qué la rechazó. Null si la última acción no fue rechazada.</param>
/// <param name="Acciones">Los estados a los que se puede pasar desde <paramref name="EstadoMostrado"/>.</param>
public sealed record CargaVista(
    Guid CargaId,
    string Descripcion,
    string Ruta,
    string Destino,
    int PesoKg,
    string EstadoServidor,
    string EstadoMostrado,
    bool PendienteDeSync,
    TipoConflicto? ConflictoRechazo,
    string? MotivoRechazo,
    IReadOnlyList<string> Acciones)
{
    public bool TieneRechazo => MotivoRechazo is not null;
}

/// <summary>Todo lo que la pantalla de cargas necesita dibujar.</summary>
/// <param name="SonDatosGuardados">True si la lista viene de la copia del teléfono y no de una consulta reciente.</param>
/// <param name="UltimaSincronizacionUtc">Último contacto real con el servidor. Null si nunca hubo.</param>
/// <param name="Aviso">Un mensaje para el conductor (por ejemplo, por qué no se pudo actualizar). Vacío si no hay.</param>
public sealed record VistaDeCargas(
    IReadOnlyList<CargaVista> Cargas, bool SonDatosGuardados, DateTime? UltimaSincronizacionUtc, string Aviso);
