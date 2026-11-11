using EcoTrace.Mobile.Core.Cola;

namespace EcoTrace.App.Modulos.Cargo;

/// <summary>
/// Una carga lista para mostrar: lo que dice el servidor, lo que el conductor ya hizo y todavía no se envió, y qué botones
/// se le pueden ofrecer ahora.
/// </summary>
/// <param name="CargaId">El identificador de la carga.</param>
/// <param name="Descripcion">Qué se transporta.</param>
/// <param name="Ruta">«Origen → Destino», ya armado para mostrar.</param>
/// <param name="Destino">A dónde llega (se usa como ubicación al entregar).</param>
/// <param name="PesoKg">El peso, en kilogramos.</param>
/// <param name="EstadoServidor">El estado que el servidor tenía la última vez que hubo conexión.</param>
/// <param name="EstadoMostrado">
/// El estado que se le muestra al conductor: el del servidor, salvo que haya una acción suya en la cola (o recién enviada)
/// que lo cambie. Así la pantalla cambia al instante, sin esperar al servidor (ADR 0005).
/// </param>
/// <param name="PendienteDeSync">True si el estado mostrado viene de una acción que todavía no llegó al servidor.</param>
/// <param name="ConflictoRechazo">El tipo de conflicto (A, B o C) si el servidor rechazó la última acción de esta carga.</param>
/// <param name="MotivoRechazo">Por qué la rechazó. Es null si la última acción no fue rechazada.</param>
/// <param name="Acciones">Los estados a los que se puede pasar desde el estado mostrado (uno por botón).</param>
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
    /// <summary>Dice si la última acción de esta carga fue rechazada por el servidor.</summary>
    public bool TieneRechazo => MotivoRechazo is not null;
}

/// <summary>Todo lo que la pantalla de cargas necesita dibujar.</summary>
/// <param name="Cargas">Las cargas, con las pendientes primero y las entregadas al final.</param>
/// <param name="SonDatosGuardados">True si la lista viene de la copia del teléfono y no de una consulta reciente.</param>
/// <param name="UltimaSincronizacionUtc">El último contacto real con el servidor. Es null si nunca hubo.</param>
/// <param name="Aviso">Un mensaje para el conductor (por ejemplo, por qué no se pudo actualizar). Vacío si no hay.</param>
public sealed record VistaDeCargas(
    IReadOnlyList<CargaVista> Cargas, bool SonDatosGuardados, DateTime? UltimaSincronizacionUtc, string Aviso);
