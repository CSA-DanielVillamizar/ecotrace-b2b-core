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
            // Primero se pinta lo guardado en el teléfono y después se intenta actualizar con el servidor.
            await _modelo.CargarAsync();
        }
        catch (Exception)
        {
            // La lista queda como estaba; el conductor puede tocar Actualizar. Una excepción aquí cerraría la app.
        }
    }
}
