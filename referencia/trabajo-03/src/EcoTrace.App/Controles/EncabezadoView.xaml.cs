namespace EcoTrace.App.Controles;

/// <summary>La franja de color de arriba de cada pantalla: etiqueta, título y una línea de apoyo.</summary>
public partial class EncabezadoView : ContentView
{
    public static readonly BindableProperty EtiquetaProperty = BindableProperty.Create(nameof(Etiqueta), typeof(string), typeof(EncabezadoView), string.Empty);
    public static readonly BindableProperty TituloProperty = BindableProperty.Create(nameof(Titulo), typeof(string), typeof(EncabezadoView), string.Empty);
    public static readonly BindableProperty SubtituloProperty = BindableProperty.Create(nameof(Subtitulo), typeof(string), typeof(EncabezadoView), string.Empty);
    public static readonly BindableProperty ExtraProperty = BindableProperty.Create(nameof(Extra), typeof(View), typeof(EncabezadoView), null);

    public EncabezadoView()
    {
        InitializeComponent();
    }

    public string Etiqueta { get => (string)GetValue(EtiquetaProperty); set => SetValue(EtiquetaProperty, value); }

    public string Titulo { get => (string)GetValue(TituloProperty); set => SetValue(TituloProperty, value); }

    public string Subtitulo { get => (string)GetValue(SubtituloProperty); set => SetValue(SubtituloProperty, value); }

    /// <summary>Contenido opcional debajo del subtítulo (por ejemplo, el indicador de conexión).</summary>
    public View? Extra { get => (View?)GetValue(ExtraProperty); set => SetValue(ExtraProperty, value); }
}
