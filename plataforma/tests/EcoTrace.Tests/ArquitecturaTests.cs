using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using EcoTrace.Billing.Infrastructure;
using EcoTrace.CargoTracking.Infrastructure;
using EcoTrace.FleetManagement.Infrastructure;
using EcoTrace.Identity.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EcoTrace.Tests;

/// <summary>
/// Pruebas de arquitectura: convierten la regla de oro del ADR 0001 ("un modulo referencia a
/// otro unicamente por su identificador") en verificaciones que fallan si alguien la rompe.
/// </summary>
public sealed class ArquitecturaTests
{
    private static readonly string[] Contextos = ["Identity", "FleetManagement", "CargoTracking", "Billing"];

    [Fact]
    public void Ningun_proyecto_referencia_a_un_proyecto_de_otro_modulo()
    {
        foreach (var proyecto in ProyectosDeCodigo())
        {
            var propio = ModuloDe(proyecto);
            foreach (var referencia in ReferenciasDeProyecto(proyecto))
            {
                Assert.True(
                    ModuloDe(referencia) == propio,
                    $"{Path.GetFileName(proyecto)} referencia a {Path.GetFileName(referencia)}, de otro modulo. " +
                    "Entre modulos solo puede viajar un identificador.");
            }
        }
    }

    [Fact]
    public void El_dominio_no_depende_de_nada()
    {
        foreach (var dominio in ProyectosDeCodigo().Where(p => p.EndsWith(".Domain.csproj", StringComparison.Ordinal)))
        {
            var xml = XDocument.Load(dominio);

            Assert.Empty(xml.Descendants("ProjectReference"));
            Assert.Empty(xml.Descendants("PackageReference"));
        }
    }

    [Fact]
    public void La_infraestructura_solo_conoce_su_propio_dominio()
    {
        foreach (var infraestructura in ProyectosDeCodigo().Where(p => p.EndsWith(".Infrastructure.csproj", StringComparison.Ordinal)))
        {
            var referencias = ReferenciasDeProyecto(infraestructura).Select(ruta => Path.GetFileName(ruta)).ToArray();

            Assert.Equal(new[] { $"{Path.GetFileNameWithoutExtension(infraestructura).Replace(".Infrastructure", ".Domain")}.csproj" }, referencias);
        }
    }

    [Fact]
    public void Ningun_proyecto_comun_es_compartido_por_los_modulos()
    {
        var carpetasDeModulo = Directory.GetDirectories(Path.Combine(RaizDeSolucion(), "src"))
            .Select(Path.GetFileName)
            .Where(nombre => nombre!.StartsWith("EcoTrace.", StringComparison.Ordinal))
            .Select(nombre => nombre!["EcoTrace.".Length..])
            .Order()
            .ToArray();

        // Console es el anfitrion de la interfaz web y no referencia ningun modulo.
        Assert.Equal(Contextos.Append("Console").Order().ToArray(), carpetasDeModulo);
    }

    [Theory]
    [MemberData(nameof(Modelos))]
    public void Todo_identificador_que_apunta_a_otro_contexto_esta_declarado_con_ReferenciaExterna(
        string contexto, IModel modelo)
    {
        foreach (var entidad in modelo.GetEntityTypes())
        {
            var clave = entidad.FindPrimaryKey()!.Properties.Select(p => p.Name).ToHashSet();
            var clavesForaneas = entidad.GetForeignKeys().SelectMany(f => f.Properties).Select(p => p.Name).ToHashSet();

            var identificadoresLibres = entidad.ClrType
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => (p.PropertyType == typeof(Guid) || p.PropertyType == typeof(Guid?)) && p.Name.EndsWith("Id", StringComparison.Ordinal))
                .Where(p => !clave.Contains(p.Name) && !clavesForaneas.Contains(p.Name));

            foreach (var propiedad in identificadoresLibres)
            {
                Assert.True(
                    TieneReferenciaExterna(propiedad),
                    $"{contexto}.{entidad.ClrType.Name}.{propiedad.Name} es un Guid que no es clave propia ni clave foranea interna. " +
                    "Si apunta a otro contexto, declare [ReferenciaExterna]; si apunta a este, modele la relacion.");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Modelos))]
    public void Una_referencia_externa_nunca_es_clave_foranea_ni_apunta_a_su_propio_contexto(
        string contexto, IModel modelo)
    {
        foreach (var entidad in modelo.GetEntityTypes())
        {
            var clavesForaneas = entidad.GetForeignKeys().SelectMany(f => f.Properties).Select(p => p.Name).ToHashSet();

            foreach (var propiedad in entidad.ClrType.GetProperties().Where(TieneReferenciaExterna))
            {
                Assert.DoesNotContain(propiedad.Name, clavesForaneas);

                var destino = propiedad.GetCustomAttributes().First(a => a.GetType().Name == "ReferenciaExternaAttribute");
                var contextoDestino = (string)destino.GetType().GetProperty("Contexto")!.GetValue(destino)!;
                Assert.Contains(contextoDestino, Contextos);
                Assert.NotEqual(contexto, contextoDestino);
            }
        }
    }

    [Fact]
    public void Identity_es_el_dominio_base_y_no_referencia_a_nadie()
    {
        var modelo = Modelos().Single(m => (string)m[0] == "Identity")[1] as IModel;

        var referencias = modelo!.GetEntityTypes()
            .SelectMany(e => e.ClrType.GetProperties())
            .Where(TieneReferenciaExterna);

        Assert.Empty(referencias);
    }

    [Fact]
    public void Cada_modulo_declara_su_propio_archivo_de_base_de_datos()
    {
        var archivos = Directory
            .GetFiles(Path.Combine(RaizDeSolucion(), "src"), "Program.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .Select(texto => Regex.Match(texto, @"AddApiDefaults<\w+>\(""(?<servicio>\w+)"",\s*""(?<archivo>[\w.]+)""\)"))
            .Where(m => m.Success)
            .Select(m => m.Groups["archivo"].Value)
            .ToArray();

        Assert.Equal(4, archivos.Length);
        Assert.Equal(4, archivos.Distinct().Count());
    }

    public static IEnumerable<object[]> Modelos()
    {
        yield return ["Identity", Construir<IdentityDbContext>(o => new IdentityDbContext(o))];
        yield return ["FleetManagement", Construir<FleetManagementDbContext>(o => new FleetManagementDbContext(o))];
        yield return ["CargoTracking", Construir<CargoTrackingDbContext>(o => new CargoTrackingDbContext(o))];
        yield return ["Billing", Construir<BillingDbContext>(o => new BillingDbContext(o))];
    }

    private static IModel Construir<TContext>(Func<DbContextOptions<TContext>, TContext> crear)
        where TContext : DbContext
    {
        var opciones = new DbContextOptionsBuilder<TContext>().UseSqlite("Data Source=:memory:").Options;
        using var contexto = crear(opciones);
        return contexto.Model;
    }

    private static bool TieneReferenciaExterna(PropertyInfo propiedad) =>
        propiedad.GetCustomAttributes().Any(a => a.GetType().Name == "ReferenciaExternaAttribute");

    private static string[] ProyectosDeCodigo() =>
        Directory.GetFiles(Path.Combine(RaizDeSolucion(), "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}EcoTrace.Console{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();

    private static IEnumerable<string> ReferenciasDeProyecto(string proyecto) =>
        XDocument.Load(proyecto).Descendants("ProjectReference")
            .Select(r => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(proyecto)!, r.Attribute("Include")!.Value.Replace('\\', Path.DirectorySeparatorChar))));

    private static string ModuloDe(string rutaProyecto)
    {
        var relativa = Path.GetRelativePath(Path.Combine(RaizDeSolucion(), "src"), rutaProyecto);
        return relativa.Split(Path.DirectorySeparatorChar)[0];
    }

    private static string RaizDeSolucion()
    {
        var directorio = new DirectoryInfo(AppContext.BaseDirectory);
        while (directorio is not null && !File.Exists(Path.Combine(directorio.FullName, "EcoTrace.sln")))
        {
            directorio = directorio.Parent;
        }

        return directorio?.FullName ?? throw new InvalidOperationException("No se encontro EcoTrace.sln.");
    }
}
