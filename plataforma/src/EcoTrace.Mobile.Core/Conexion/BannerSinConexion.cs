using System.ComponentModel;
using EcoTrace.Mobile.Core.Cola;
using EcoTrace.Mobile.Core.Sincronizacion;

namespace EcoTrace.Mobile.Core.Conexion;

/// <summary>
/// El banner de "Sin conexión": un solo componente para todas las pantallas (ADR 0005). La lógica y el texto
/// viven aquí para que se prueben sin emulador; la vista de MAUI (<c>BannerSinConexionView</c>) solo lo muestra.
/// <para>Ejemplo: "Sin conexión. Mostrando datos guardados. Última sincronización: 10:30 a. m."</para>
/// </summary>
public sealed class BannerSinConexion : INotifyPropertyChanged
{
    private readonly IConectividad _conectividad;
    private readonly IColaLocal _cola;
    private readonly TimeProvider _reloj;
    private bool _visible;
    private string _texto = string.Empty;

    public BannerSinConexion(IConectividad conectividad, IColaLocal cola, TimeProvider? reloj = null)
    {
        _conectividad = conectividad;
        _cola = cola;
        _reloj = reloj ?? TimeProvider.System;
        _conectividad.CambioDeConexion += async (_, _) => await ActualizarAsync();
        _cola.Cambio += async (_, _) => await ActualizarAsync();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Solo se ve cuando no hay señal.</summary>
    public bool Visible
    {
        get => _visible;
        private set => Asignar(ref _visible, value, nameof(Visible));
    }

    public string Texto
    {
        get => _texto;
        private set => Asignar(ref _texto, value, nameof(Texto));
    }

    public async Task ActualizarAsync()
    {
        var ultima = await _cola.UltimaSincronizacionAsync();
        Texto = ConstruirTexto(ultima, _reloj.GetUtcNow().UtcDateTime, TimeZoneInfo.Local);
        Visible = !_conectividad.HayConexion;
    }

    public static string ConstruirTexto(DateTime? ultimaSincronizacionUtc, DateTime ahoraUtc, TimeZoneInfo zona)
    {
        const string inicio = "Sin conexión. Mostrando datos guardados.";
        if (ultimaSincronizacionUtc is null)
        {
            return $"{inicio} Aún no hay una sincronización.";
        }

        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(ultimaSincronizacionUtc.Value, DateTimeKind.Utc), zona);
        var hoy = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(ahoraUtc, DateTimeKind.Utc), zona).Date;
        var hora = FormatearHora(local);
        var cuando = local.Date == hoy ? hora : $"{local.Day:00}/{local.Month:00} {hora}";
        return $"{inicio} Última sincronización: {cuando}";
    }

    /// <summary>"10:30 a. m." con el formato del idioma, escrito a mano para que no cambie según el teléfono.</summary>
    public static string FormatearHora(DateTime local)
    {
        var hora12 = local.Hour % 12 == 0 ? 12 : local.Hour % 12;
        var sufijo = local.Hour < 12 ? "a. m." : "p. m.";
        return $"{hora12}:{local.Minute:00} {sufijo}";
    }

    private void Asignar<T>(ref T campo, T valor, string nombre)
    {
        if (EqualityComparer<T>.Default.Equals(campo, valor))
        {
            return;
        }

        campo = valor;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nombre));
    }
}
