using System.ComponentModel;
using System.Runtime.CompilerServices;
using EcoTrace.Mobile.Core.Modulos;
using EcoTrace.Mobile.Core.Sincronizacion;

namespace EcoTrace.App.Servicios;

/// <summary>Lee el <c>appsettings.json</c> que el build incrustó: el de desarrollo en Debug y el de producción en Release.</summary>
public static class ConfiguracionDelEntorno
{
    public static ConfiguracionServicios Cargar()
    {
        var ensamblado = typeof(ConfiguracionDelEntorno).Assembly;
        using var flujo = ensamblado.GetManifestResourceStream("appsettings.json")
            ?? throw new InvalidOperationException("La app no trae su appsettings.json incrustado.");
        using var lector = new StreamReader(flujo);
        return ConfiguracionServicios.Leer(lector.ReadToEnd());
    }
}

/// <summary>Conectividad real del teléfono, vista por el Mobile.Core a través de <see cref="IConectividad"/>.</summary>
public sealed class ConectividadMaui : IConectividad
{
    public ConectividadMaui() =>
        Connectivity.Current.ConnectivityChanged += (_, e) =>
            CambioDeConexion?.Invoke(this, e.NetworkAccess == NetworkAccess.Internet);

    public bool HayConexion => Connectivity.Current.NetworkAccess == NetworkAccess.Internet;

    public event EventHandler<bool>? CambioDeConexion;
}

/// <summary>Base mínima para los ViewModels de la app.</summary>
public abstract class ObservableBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Asignar<T>(ref T campo, T valor, [CallerMemberName] string? nombre = null)
    {
        if (EqualityComparer<T>.Default.Equals(campo, valor))
        {
            return false;
        }

        campo = valor;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nombre));
        return true;
    }

    protected void Notificar([CallerMemberName] string? nombre = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nombre));
}

/// <summary>Colores de estado de la guía de marca, para los pocos lugares donde se calculan en código.</summary>
public static class Paleta
{
    public static readonly Color InfoTexto = Color.FromArgb("#0F5A73");
    public static readonly Color InfoFondo = Color.FromArgb("#E3F1F6");
    public static readonly Color CorrectoTexto = Color.FromArgb("#1E6B3F");
    public static readonly Color CorrectoFondo = Color.FromArgb("#E7F3EB");
    public static readonly Color ErrorTexto = Color.FromArgb("#A83226");
    public static readonly Color ErrorFondo = Color.FromArgb("#FBEAE8");
    public static readonly Color AdvertenciaTexto = Color.FromArgb("#7A4B00");
    public static readonly Color AdvertenciaFondo = Color.FromArgb("#FFF3DC");
    public static readonly Color NeutroTexto = Color.FromArgb("#3F4F4C");
    public static readonly Color NeutroFondo = Color.FromArgb("#ECEFEE");
}
