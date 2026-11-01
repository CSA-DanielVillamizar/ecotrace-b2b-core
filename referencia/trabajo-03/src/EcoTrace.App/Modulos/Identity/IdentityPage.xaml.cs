using EcoTrace.App.ViewModels;

namespace EcoTrace.App.Modulos.Identity;

public partial class IdentityPage : ContentPage
{
    private readonly PerfilViewModel _modelo;

    public IdentityPage(PerfilViewModel modelo)
    {
        InitializeComponent();
        BindingContext = _modelo = modelo;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        try
        {
            await _modelo.ActualizarAsync();
        }
        catch (Exception)
        {
            // La pantalla se vuelve a leer con el próximo cambio de sesión.
        }
    }
}
