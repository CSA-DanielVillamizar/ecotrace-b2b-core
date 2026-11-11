using EcoTrace.App.ViewModels;

namespace EcoTrace.App.Modulos.Fleet;

public partial class FleetPage : ContentPage
{
    private readonly FleetViewModel _modelo;

    public FleetPage(FleetViewModel modelo)
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
