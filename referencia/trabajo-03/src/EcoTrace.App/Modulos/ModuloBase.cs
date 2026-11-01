using EcoTrace.Mobile.Core.Modulos;
using EcoTrace.Mobile.Core.Sincronizacion;

namespace EcoTrace.App.Modulos;

/// <summary>
/// Punto de partida de cada módulo: qué es, qué debe entregar según la especificación y qué acciones encola.
/// Las propiedades de texto alimentan la tarjeta del esqueleto (<c>GuiaDelModuloView</c>), que cada escuadrón reemplaza
/// por sus pantallas; <see cref="Manejadores"/> es lo que el escuadrón llena cuando su módulo ya encola acciones.
/// </summary>
public abstract class ModuloBase : IModuloApp
{
    public abstract string Nombre { get; }

    public abstract string Titulo { get; }

    /// <summary>Escuadrón dueño, para la etiqueta del encabezado.</summary>
    public abstract string Escuadron { get; }

    public abstract string Descripcion { get; }

    public abstract string Muestra { get; }

    public abstract string AccionEncolada { get; }

    public abstract string ConflictoDeEjemplo { get; }

    public string PistaDeCarpeta => $"Esto es el esqueleto. Tu escuadrón trabaja solo en EcoTrace.App/Modulos/{Nombre}/.";

    /// <summary>Un manejador por cada tipo de acción que el módulo encola. Vacío mientras no encole nada.</summary>
    public virtual IReadOnlyList<IManejadorOperacion> Manejadores => [];
}
