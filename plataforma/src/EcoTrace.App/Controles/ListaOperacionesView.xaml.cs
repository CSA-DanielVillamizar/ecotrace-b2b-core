using EcoTrace.App.ViewModels;
using EcoTrace.Mobile.Core.Cola;

namespace EcoTrace.App.Controles;

/// <summary>
/// La cola local en pantalla: cada acción con su estado, su conflicto y su motivo. Si se le da un <see cref="Modulo"/>,
/// muestra solo las de ese módulo.
/// </summary>
public partial class ListaOperacionesView : ContentView
{
    public static readonly BindableProperty ModuloProperty =
        BindableProperty.Create(nameof(Modulo), typeof(string), typeof(ListaOperacionesView), null);

    private ListaOperacionesViewModel? _modelo;

    public ListaOperacionesView()
    {
        InitializeComponent();
    }

    /// <summary>Nombre del módulo cuyas acciones se muestran; vacío para mostrar todas.</summary>
    public string? Modulo { get => (string?)GetValue(ModuloProperty); set => SetValue(ModuloProperty, value); }

    protected override async void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        if (Handler is null)
        {
            _modelo?.Desconectar();
            return;
        }

        var cola = Handler.MauiContext?.Services.GetService<IColaLocal>();
        if (cola is null)
        {
            return;
        }

        _modelo = new ListaOperacionesViewModel(cola, string.IsNullOrWhiteSpace(Modulo) ? null : Modulo, MainThread.BeginInvokeOnMainThread);
        BindingContext = _modelo;
        _modelo.Conectar();

        try
        {
            await _modelo.CargarAsync();
        }
        catch (Exception)
        {
            // Si no se puede leer la cola, la lista queda vacía; no se cae la pantalla.
        }
    }
}
