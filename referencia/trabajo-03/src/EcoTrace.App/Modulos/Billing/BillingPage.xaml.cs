using EcoTrace.App.ViewModels;

namespace EcoTrace.App.Modulos.Billing;

public partial class BillingPage : ContentPage
{
    private readonly BillingViewModel _modelo;

    public BillingPage(BillingViewModel modelo)
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
