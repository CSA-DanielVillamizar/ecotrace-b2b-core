using EcoTrace.App.ViewModels;

namespace EcoTrace.App.Paginas;

public partial class InicioPage : ContentPage
{
    private readonly InicioViewModel _modelo;

    public InicioPage(InicioViewModel modelo)
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
            // Los contadores se vuelven a leer con el próximo cambio de la cola.
        }
    }
}
