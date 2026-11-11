using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.CargoTracking.Api.Extensions;

/// <summary>
/// Describe el Bounded Context leyendo el modelo real de EF Core y los atributos
/// [ReferenciaExterna] del dominio. La consola web dibuja el mapa de contextos con estos
/// datos, asi que el diagrama no puede desincronizarse del codigo.
/// </summary>
public static class ContextoMetadata
{
    private const string NombreAtributo = "ReferenciaExternaAttribute";

    public static object Describir(DbContext db, string servicio)
    {
        var cadena = new SqliteConnectionStringBuilder(db.Database.GetConnectionString());

        var entidades = db.Model.GetEntityTypes()
            .Where(e => !e.IsOwned())
            .OrderBy(e => e.ClrType.Name)
            .Select(e => new
            {
                nombre = e.ClrType.Name,
                tabla = e.GetTableName(),
                clave = e.FindPrimaryKey()!.Properties.Select(p => p.Name).ToArray(),
                propiedades = e.GetProperties()
                    .Select(p => new { nombre = p.Name, tipo = NombreDeTipo(p.ClrType) })
                    .ToArray(),
                referenciasExternas = e.ClrType.GetProperties()
                    .Select(p => (Propiedad: p, Atributo: p.GetCustomAttributes()
                        .FirstOrDefault(a => a.GetType().Name == NombreAtributo)))
                    .Where(x => x.Atributo is not null)
                    .Select(x => new
                    {
                        campo = x.Propiedad.Name,
                        contexto = Leer(x.Atributo!, "Contexto"),
                        descripcion = Leer(x.Atributo!, "Descripcion")
                    })
                    .ToArray(),
                relacionesInternas = e.GetForeignKeys()
                    .Select(f => new
                    {
                        campo = string.Join(", ", f.Properties.Select(p => p.Name)),
                        destino = f.PrincipalEntityType.ClrType.Name
                    })
                    .ToArray()
            })
            .ToArray();

        return new
        {
            contexto = servicio,
            baseDeDatos = Path.GetFileName(cadena.DataSource),
            motor = "SQLite",
            entidades
        };
    }

    private static string NombreDeTipo(Type tipo) =>
        Nullable.GetUnderlyingType(tipo) is { } subyacente ? subyacente.Name + "?" : tipo.Name;

    private static string Leer(Attribute atributo, string propiedad) =>
        atributo.GetType().GetProperty(propiedad)?.GetValue(atributo) as string ?? string.Empty;
}
