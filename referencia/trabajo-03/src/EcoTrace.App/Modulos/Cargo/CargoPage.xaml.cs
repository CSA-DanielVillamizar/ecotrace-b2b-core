using EcoTrace.App.ViewModels;

namespace EcoTrace.App.Modulos.Cargo;

public partial class CargoPage : ContentPage
{
    private readonly CargoViewModel _modelo;

    public CargoPage(CargoViewModel modelo)
    {
        InitializeComponent();
        BindingContext = _modelo = modelo;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        try
        {
            await _modelo.AlAparecerAsync();
        }
        catch (Exception)
        {
            // La pantalla se refresca con el botón; un fallo al cargar no debe cerrar la app.
        }
    }
}
