using EcoTrace.Identity.Domain;
using EcoTrace.Identity.Infrastructure;
using EcoTrace.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EcoTrace.Tests;

/// <summary>
/// <c>Tenant.Version</c> sube en uno cada vez que cambia el estado y sirve a quien consume el cambio para ignorar uno atrasado.
/// Para que eso valga, Identity no puede guardar un cambio hecho sobre una copia vieja: si lo hiciera, una suspensión leída
/// cuando la organización estaba en la versión 1 pisaría una reactivación ya guardada en la versión 3, y además bajaría la versión.
/// </summary>
public sealed class ConcurrenciaDeTenantTests(ApiFactory<IdentityApiMarker> fabrica) : IClassFixture<ApiFactory<IdentityApiMarker>>
{
    private async Task<Guid> CrearOrganizacionAsync()
    {
        var respuesta = await fabrica.ComoOperador().PostearAsync("/api/tenants",
            new { nombre = $"Transportes {Guid.NewGuid():N}"[..22], tenantType = "Transportista" });
        return respuesta.Id("tenantId");
    }

    [Fact]
    public async Task Una_suspension_leida_antes_de_una_reactivacion_no_la_pisa_ni_baja_la_version()
    {
        var id = await CrearOrganizacionAsync();

        // Una solicitud lee la organización activa (versión 1) y se queda esperando…
        using var alcanceViejo = fabrica.Services.CreateScope();
        var dbViejo = alcanceViejo.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var copiaVieja = await dbViejo.Tenants.SingleAsync(t => t.TenantId == id);
        Assert.Equal(1, copiaVieja.Version);

        // …mientras otra la suspende (versión 2) y la reactiva (versión 3).
        using (var alcanceNuevo = fabrica.Services.CreateScope())
        {
            var db = alcanceNuevo.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var tenant = await db.Tenants.SingleAsync(t => t.TenantId == id);
            Assert.True(tenant.Suspender());
            await db.GuardarAsync("No se pudo suspender.");
            Assert.True(tenant.Reactivar());
            await db.GuardarAsync("No se pudo reactivar.");
            Assert.Equal(3, tenant.Version);
        }

        // La solicitud vieja intenta suspender con su copia de la versión 1: choca (conflicto C) y no guarda nada.
        Assert.True(copiaVieja.Suspender());
        var choque = await Assert.ThrowsAsync<DomainException>(() => dbViejo.GuardarAsync("No se pudo suspender."));
        Assert.Equal(TipoConflicto.C, choque.Tipo);

        // Lo guardado sigue siendo la reactivación: activa y en la versión 3, no suspendida ni en la 2.
        using var alcanceFinal = fabrica.Services.CreateScope();
        var dbFinal = alcanceFinal.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var guardado = await dbFinal.Tenants.AsNoTracking().SingleAsync(t => t.TenantId == id);
        Assert.Equal(EstadoTenant.Activo, guardado.Estado);
        Assert.Equal(3, guardado.Version);
    }

    [Fact]
    public async Task Dos_cambios_seguidos_con_la_copia_al_dia_si_se_guardan_y_suben_la_version_de_uno_en_uno()
    {
        var id = await CrearOrganizacionAsync();
        var operador = fabrica.ComoOperador();

        Assert.Equal(2, (await operador.PostearAsync($"/api/tenants/{id}/suspension")).Propiedad("version").GetInt32());
        Assert.Equal(3, (await operador.PostearAsync($"/api/tenants/{id}/reactivacion")).Propiedad("version").GetInt32());
        // Repetir no cambia nada: idempotente, misma versión.
        Assert.Equal(3, (await operador.PostearAsync($"/api/tenants/{id}/reactivacion")).Propiedad("version").GetInt32());
    }
}
