namespace EcoTrace.App.Modulos.Billing;

public partial class BillingPage : ContentPage
{
    public BillingPage(ModuloBilling modulo)
    {
        InitializeComponent();
        BindingContext = modulo;
    }
}
