using EcoTrace.Mobile.Core.Conexion;

namespace EcoTrace.App.Controles;

/// <summary>La vista del banner "Sin conexión". La lógica y el texto están en <see cref="BannerSinConexion"/> (Mobile.Core).</summary>
public partial class BannerSinConexionView : ContentView
{
    public BannerSinConexionView()
    {
        InitializeComponent();
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        if (Handler?.MauiContext?.Services.GetService<BannerSinConexion>() is { } banner)
        {
            BindingContext = banner;
            _ = RefrescarAsync(banner);
        }
    }

    private static async Task RefrescarAsync(BannerSinConexion banner)
    {
        try
        {
            await banner.ActualizarAsync();
        }
        catch (Exception)
        {
            // Sin banner no se cae la pantalla; se actualiza con el próximo cambio de conexión.
        }
    }
}
