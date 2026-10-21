using System.Windows.Input;
using EcoTrace.App.Servicios;
using EcoTrace.Mobile.Core.Sincronizacion;

namespace EcoTrace.App.Modulos.Cargo;

/// <summary>Un botón de una carga, por ejemplo "Confirmar entrega". Al tocarlo se guarda la acción en la cola.</summary>
public sealed class AccionFila(string texto, ICommand comando)
{
    public string Texto { get; } = texto;

    public ICommand Comando { get; } = comando;
}

/// <summary>
/// Una carga tal como se dibuja en la pantalla: textos ya armados y los colores de estado de la guía de marca.
/// Es solo presentación; las reglas (qué estado se muestra, qué botones se ofrecen) viven en
/// <see cref="ServicioDeCargas"/> y se prueban sin emulador.
/// </summary>
public sealed class CargaFila
{
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

    public string Descripcion { get; }

    public string Ruta { get; }

    public string PesoTexto { get; }

    public string EstadoTexto { get; }

    public Color EstadoFondo { get; }

    public Color EstadoColor { get; }

    /// <summary>El estado que se ve todavía no llegó al servidor: la acción está guardada en el teléfono.</summary>
    public bool PendienteDeSync { get; }

    public bool TieneRechazo { get; }

    /// <summary>Por ejemplo "Conflicto B · el servidor decidió; la acción se conserva".</summary>
    public string RechazoTitulo { get; }

    public string RechazoMotivo { get; }

    public IReadOnlyList<AccionFila> Acciones { get; }

    public bool TieneAcciones => Acciones.Count > 0;
}
