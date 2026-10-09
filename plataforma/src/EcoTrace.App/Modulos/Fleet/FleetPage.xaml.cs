namespace EcoTrace.App.Modulos.Fleet;

public partial class FleetPage : ContentPage
{
    public FleetPage(ModuloFleet modulo)
    {
        InitializeComponent();
        BindingContext = modulo;
    }
}
