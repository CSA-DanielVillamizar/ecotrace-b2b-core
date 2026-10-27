namespace EcoTrace.App.Modulos.Identity;

public partial class IdentityPage : ContentPage
{
    private readonly PerfilViewModel _modelo;

    public IdentityPage(PerfilViewModel modelo)
    {
        InitializeComponent();
        modelo.Confirmar = (titulo, mensaje) => DisplayAlert(titulo, mensaje, "Aceptar", "Cancelar");
        BindingContext = _modelo = modelo;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _modelo.Conectar();

        try
        {
            await _modelo.AlAparecerAsync();
        }
        catch (Exception)
        {
            // Sin datos legibles la pantalla queda en su estado anterior; se vuelve a leer con el próximo cambio.
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _modelo.Desconectar();
    }
}
