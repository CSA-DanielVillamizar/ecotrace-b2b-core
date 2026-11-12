namespace EcoTrace.App.Modulos.Cargo;

/// <summary>
/// La pantalla de cargas del conductor. El dibujo está en <c>CargoPage.xaml</c> y el comportamiento en
/// <see cref="CargoViewModel"/>; esta clase solo los conecta y carga los datos cuando la pantalla aparece.
/// </summary>
public partial class CargoPage : ContentPage
{
    private readonly CargoViewModel _modelo;

    /// <summary>Conecta la pantalla con su ViewModel, que le da las cargas y los botones.</summary>
    public CargoPage(CargoViewModel modelo)
    {
        InitializeComponent();
        BindingContext = _modelo = modelo;
    }

    /// <summary>Se ejecuta cada vez que la pantalla aparece: muestra lo guardado y después actualiza con el servidor.</summary>
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
