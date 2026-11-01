using EcoTrace.Mobile.Core.Cola;
using EcoTrace.Mobile.Core.Sincronizacion;

namespace EcoTrace.App.Servicios;

/// <summary>
/// Un módulo que sabe corregir una acción suya que el servidor rechazó por un conflicto de tipo A (el dato no cumple una regla
/// pero se puede arreglar). Lo registra cada módulo, igual que su <c>IModuloApp</c>.
/// </summary>
public interface ICorreccionDeModulo
{
    string Modulo { get; }

    string Tipo { get; }

    /// <summary>Lleva a la pantalla del módulo el dato rechazado para que la persona lo corrija y lo envíe de nuevo.</summary>
    Task CorregirAsync(OperacionPendiente rechazada);
}

public sealed class CorreccionDeModulo(string modulo, string tipo, Func<OperacionPendiente, Task> corregir) : ICorreccionDeModulo
{
    public string Modulo => modulo;

    public string Tipo => tipo;

    public Task CorregirAsync(OperacionPendiente rechazada) => corregir(rechazada);
}

/// <summary>
/// Dice qué acciones rechazadas se pueden corregir y reintentar (solo las de conflicto A: un B lo decidió el servidor y un C pide
/// revisión) y entrega la acción al módulo que sabe corregirla.
/// </summary>
public sealed class Correcciones(IEnumerable<ICorreccionDeModulo> modulos)
{
    private readonly List<ICorreccionDeModulo> _modulos = modulos.ToList();

    public bool Puede(OperacionPendiente operacion) =>
        operacion.Estado == EstadoOperacion.Rechazado && operacion.TipoConflicto == TipoConflicto.A && Buscar(operacion) is not null;

    public Task CorregirAsync(OperacionPendiente operacion) =>
        (Puede(operacion) ? Buscar(operacion)! : throw new InvalidOperationException("Esta acción no se puede corregir."))
            .CorregirAsync(operacion);

    private ICorreccionDeModulo? Buscar(OperacionPendiente operacion) =>
        _modulos.FirstOrDefault(m => m.Modulo == operacion.Modulo && m.Tipo == operacion.Tipo);
}
