namespace EcoTrace.App.Modulos.Cargo;

public partial class CargoPage : ContentPage
{
    public CargoPage(ModuloCargo modulo)
    {
        InitializeComponent();
        BindingContext = modulo;
    }
}
