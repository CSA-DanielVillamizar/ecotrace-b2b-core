namespace EcoTrace.App.Modulos.Billing;

public partial class BillingPage : ContentPage
{
    private readonly ModuloBilling _modulo;

    public BillingPage(ModuloBilling modulo)
    {
        InitializeComponent();
        BindingContext = modulo;
        _modulo = modulo;
    }

    private async void OnGuardarEvidenciaClicked(object? sender, EventArgs e)
    {
        var textoPagoId = PagoIdEntry.Text?.Trim();
        var textoEvidencia = EvidenciaEditor.Text?.Trim();

        if (string.IsNullOrWhiteSpace(textoPagoId) || !Guid.TryParse(textoPagoId, out var pagoId))
        {
            await DisplayAlert("Dato requerido", "Por favor ingresa un identificador de pago válido (GUID).", "Entendido");
            return;
        }

        if (string.IsNullOrWhiteSpace(textoEvidencia))
        {
            await DisplayAlert("Dato requerido", "Por favor describe la evidencia o número de remisión.", "Entendido");
            return;
        }

        await _modulo.EncolarEvidenciaAsync(pagoId, textoEvidencia);

        PagoIdEntry.Text = string.Empty;
        EvidenciaEditor.Text = string.Empty;

        await DisplayAlert(
            "Acción guardada",
            "La evidencia de entrega quedó encolada localmente y se enviará al servidor al sincronizar.",
            "OK");
    }
}
