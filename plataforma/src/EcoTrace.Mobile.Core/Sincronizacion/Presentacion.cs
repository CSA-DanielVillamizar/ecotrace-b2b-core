using System.Text;
using EcoTrace.Mobile.Core.Cola;

namespace EcoTrace.Mobile.Core.Sincronizacion;

/// <summary>
/// Los textos que la app le muestra al conductor sobre la cola. Están aquí y no en las pantallas para que los cuatro
/// módulos hablen igual y se prueben sin emulador.
/// </summary>
public static class Presentacion
{
    public static string Estado(EstadoOperacion estado) => estado switch
    {
        EstadoOperacion.PendienteSync => "En cola",
        EstadoOperacion.Sincronizando => "Enviando",
        EstadoOperacion.Sincronizado => "Sincronizada",
        EstadoOperacion.Rechazado => "Rechazada",
        _ => estado.ToString()
    };

    /// <summary>Qué significa el conflicto y qué puede hacer el conductor.</summary>
    public static string Conflicto(TipoConflicto? tipo) => tipo switch
    {
        TipoConflicto.A => "Conflicto A · el dato se puede corregir y reintentar",
        TipoConflicto.B => "Conflicto B · el servidor decidió; la acción se conserva",
        TipoConflicto.C => "Conflicto C · otra persona cambió lo mismo; pide revisión",
        _ => string.Empty
    };

    /// <summary>"ConfirmarEntrega" se muestra como "Confirmar entrega".</summary>
    public static string Accion(string tipo)
    {
        if (string.IsNullOrWhiteSpace(tipo))
        {
            return string.Empty;
        }

        var texto = new StringBuilder(tipo.Length + 4);
        for (var i = 0; i < tipo.Length; i++)
        {
            var letra = tipo[i];
            if (i > 0 && char.IsUpper(letra) && !char.IsUpper(tipo[i - 1]))
            {
                texto.Append(' ');
                texto.Append(char.ToLowerInvariant(letra));
            }
            else
            {
                texto.Append(i == 0 ? char.ToUpperInvariant(letra) : letra);
            }
        }

        return texto.ToString();
    }

    /// <summary>Lo que se le dice al conductor cuando termina (o no puede empezar) una sincronización.</summary>
    public static string Resultado(ResultadoSincronizacion r) => r.Parada switch
    {
        ParadaSincronizacion.Ninguna when r.Sincronizadas + r.Rechazadas == 0 => "No había nada por enviar.",
        ParadaSincronizacion.Ninguna => Resumen(r),
        ParadaSincronizacion.SinConexion => $"Sin conexión. {Pendientes(r.Pendientes)} hasta que vuelva la señal.",
        ParadaSincronizacion.SinSesion => $"Inicia sesión para enviar. {Pendientes(r.Pendientes)}.",
        ParadaSincronizacion.TokenRechazado => $"El servidor no aceptó tu sesión. {Pendientes(r.Pendientes)}.",
        ParadaSincronizacion.ErrorTransitorio => $"El servidor no respondió. Se reintenta solo. {Pendientes(r.Pendientes)}.",
        ParadaSincronizacion.YaEnCurso => "Ya se está sincronizando.",
        ParadaSincronizacion.Cancelada => "Sincronización cancelada.",
        _ => string.Empty
    };

    private static string Resumen(ResultadoSincronizacion r)
    {
        var partes = new List<string>();
        if (r.Sincronizadas > 0)
        {
            partes.Add(r.Sincronizadas == 1 ? "1 acción enviada" : $"{r.Sincronizadas} acciones enviadas");
        }

        if (r.Rechazadas > 0)
        {
            partes.Add(r.Rechazadas == 1 ? "1 rechazada por el servidor" : $"{r.Rechazadas} rechazadas por el servidor");
        }

        return string.Join(" · ", partes) + ".";
    }

    private static string Pendientes(int cantidad) => cantidad switch
    {
        0 => "No quedan acciones en cola",
        1 => "1 acción espera en cola",
        _ => $"{cantidad} acciones esperan en cola"
    };
}
