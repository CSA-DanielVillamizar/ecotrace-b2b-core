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
            // Primero se pinta lo guardado en el teléfono y después se intenta actualizar con Fleet.
            await _modelo.CargarAsync();
        }
        catch (Exception)
        {
            // La pantalla queda con lo que tenía; el conductor puede tocar Actualizar. Una excepción aquí cerraría la app.
        }
    }

    private async void AlActualizar(object? sender, EventArgs e)
    {
        try
        {
            await _modelo.ActualizarAsync();
        }
        catch (Exception)
        {
            // Igual que al abrir: se queda con lo guardado.
        }
    }

    private async void AlReportar(object? sender, EventArgs e)
    {
        try
        {
            await _modelo.ReportarAsync();
        }
        catch (Exception)
        {
            // Si la novedad alcanzó a guardarse, sigue en la cola y el motor la envía después.
        }
    }
}
