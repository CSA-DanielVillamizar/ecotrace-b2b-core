using System.Windows.Input;
using EcoTrace.App.Servicios;
using EcoTrace.Mobile.Core.Sincronizacion;

namespace EcoTrace.App.Modulos.Cargo;

/// <summary>Un botón de una carga, por ejemplo «Confirmar entrega». Al tocarlo se guarda la acción en la cola.</summary>
/// <param name="texto">Lo que dice el botón.</param>
/// <param name="comando">Lo que hace el botón al tocarlo.</param>
public sealed class AccionFila(string texto, ICommand comando)
{
    /// <summary>Lo que dice el botón.</summary>
    public string Texto { get; } = texto;

    /// <summary>Lo que hace el botón al tocarlo.</summary>
    public ICommand Comando { get; } = comando;
}

/// <summary>
/// Una carga tal como se dibuja en la pantalla: los textos ya armados y los colores de estado de la guía de marca.
/// Es solo presentación: las reglas (qué estado mostrar, qué botones ofrecer) viven en <see cref="ServicioDeCargas"/> y
/// se prueban sin emulador.
/// </summary>
public sealed class CargaFila
{
    /// <summary>
    /// Arma la fila a partir de una carga ya lista para mostrar: los textos, el color según el estado y un botón por cada
    /// cambio de estado posible.
    /// </summary>
    /// <param name="carga">La carga a dibujar.</param>
    /// <param name="alElegirAccion">Qué hacer cuando el conductor toca uno de los botones (recibe la carga y el estado elegido).</param>
    public CargaFila(CargaVista carga, Func<CargaVista, string, Task> alElegirAccion)
    {
        Descripcion = carga.Descripcion;
        Ruta = carga.Ruta;
        PesoTexto = $"{carga.PesoKg:N0} kg";
        EstadoTexto = EstadosDeCarga.Texto(carga.EstadoMostrado);
        PendienteDeSync = carga.PendienteDeSync;
        TieneRechazo = carga.TieneRechazo;
        RechazoTitulo = Presentacion.Conflicto(carga.ConflictoRechazo);
        RechazoMotivo = carga.MotivoRechazo ?? string.Empty;

        (EstadoFondo, EstadoColor) = carga.EstadoMostrado switch
        {
            EstadosDeCarga.EnTransito => (Paleta.InfoFondo, Paleta.InfoTexto),
            EstadosDeCarga.ConNovedad => (Paleta.AdvertenciaFondo, Paleta.AdvertenciaTexto),
            EstadosDeCarga.Entregado => (Paleta.CorrectoFondo, Paleta.CorrectoTexto),
            _ => (Paleta.NeutroFondo, Paleta.NeutroTexto)
        };

        Acciones = carga.Acciones
            .Select(destino => new AccionFila(
                EstadosDeCarga.Boton(destino),
                new Command(async () => await alElegirAccion(carga, destino))))
            .ToList();
    }

    /// <summary>Qué se transporta.</summary>
    public string Descripcion { get; }

    /// <summary>«Origen → Destino».</summary>
    public string Ruta { get; }

    /// <summary>El peso ya escrito, por ejemplo «4.200 kg».</summary>
    public string PesoTexto { get; }

    /// <summary>El estado ya escrito, por ejemplo «En tránsito».</summary>
    public string EstadoTexto { get; }

    /// <summary>El color de fondo de la etiqueta del estado.</summary>
    public Color EstadoFondo { get; }

    /// <summary>El color del texto de la etiqueta y de la franja lateral de la tarjeta.</summary>
    public Color EstadoColor { get; }

    /// <summary>True si el estado que se ve todavía no llegó al servidor: la acción está guardada en el teléfono.</summary>
    public bool PendienteDeSync { get; }

    /// <summary>True si el servidor rechazó la última acción de esta carga.</summary>
    public bool TieneRechazo { get; }

    /// <summary>El título del rechazo, por ejemplo «Conflicto B · el servidor decidió; la acción se conserva».</summary>
    public string RechazoTitulo { get; }

    /// <summary>Por qué la rechazó el servidor, con sus palabras.</summary>
    public string RechazoMotivo { get; }

    /// <summary>Los botones de esta carga (uno por cada cambio de estado posible).</summary>
    public IReadOnlyList<AccionFila> Acciones { get; }

    /// <summary>True si la carga tiene al menos un botón que mostrar.</summary>
    public bool TieneAcciones => Acciones.Count > 0;
}
