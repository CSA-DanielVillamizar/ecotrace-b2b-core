namespace EcoTrace.App.Modulos.Identity;

public partial class IdentityPage : ContentPage
{
    public IdentityPage(ModuloIdentity modulo)
    {
        InitializeComponent();
        BindingContext = modulo;
    }
}
