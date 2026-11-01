using System.Net;
using System.Security.Cryptography;
using EcoTrace.Tests.TestSupport;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EcoTrace.Tests;

/// <summary>
/// Cómo un Resource Server (aquí, Fleet) trata la clave pública de Identity: la baja una vez y la guarda en memoria, la
/// renueva cuando aparece un <c>kid</c> que no conoce (con un mínimo entre renovaciones) y, si Identity cae, un token
/// válido sigue validándose con la copia que ya tiene. Es lo que permite validar "local, sin red" (ADR 0004).
/// </summary>
public sealed class ClavesPublicasDeIdentityTests : IAsyncLifetime
{
    private ApiFactory<FleetManagementApiMarker> _identityDeMentira = null!;
    private WebApplicationFactory<FleetManagementApiMarker> _fleet = null!;

    public Task InitializeAsync()
    {
        _identityDeMentira = new ApiFactory<FleetManagementApiMarker>();

        // Un segundo entre renovaciones forzadas, para no esperar minutos en la prueba. En producción son 30 segundos.
        _fleet = _identityDeMentira.WithWebHostBuilder(b => b.UseSetting("Jwt:MinimoEntreRenovacionesSegundos", "1"));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _fleet.Dispose();
        await _identityDeMentira.DisposeAsync();
    }

    private JwksFalso Identity => _identityDeMentira.Jwks;

    private async Task<HttpStatusCode> LeerAsync(string token) =>
        (await _fleet.CreateClient().ConToken(token).GetAsync("/api/vehiculos")).StatusCode;

    private static string Token(RSA? clave = null, string? kid = null) =>
        SeguridadDePrueba.Emitir(Rol.Supervisor, Guid.NewGuid(), firmarCon: clave, kid: kid);

    [Fact]
    public async Task El_JWKS_se_baja_una_sola_vez_y_no_en_cada_solicitud()
    {
        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(HttpStatusCode.OK, await LeerAsync(Token()));
        }

        Assert.Equal(1, Identity.Llamadas);
    }

    [Fact]
    public async Task Si_Identity_cae_despues_de_la_primera_descarga_un_token_valido_sigue_validandose()
    {
        Assert.Equal(HttpStatusCode.OK, await LeerAsync(Token()));

        Identity.Caido = true;

        Assert.Equal(HttpStatusCode.OK, await LeerAsync(Token()));
        Assert.Equal(HttpStatusCode.OK, await LeerAsync(Token()));
    }

    [Fact]
    public async Task Sin_ninguna_copia_de_la_clave_el_modulo_falla_cerrado_y_se_recupera_cuando_Identity_vuelve()
    {
        Identity.Caido = true;
        Assert.Equal(HttpStatusCode.Unauthorized, await LeerAsync(Token()));

        Identity.Caido = false;
        await Task.Delay(TimeSpan.FromMilliseconds(1300));

        Assert.Equal(HttpStatusCode.OK, await LeerAsync(Token()));
    }

    [Fact]
    public async Task Cuando_Identity_rota_su_clave_un_kid_nuevo_hace_que_el_modulo_vuelva_a_bajar_el_JWKS()
    {
        Assert.Equal(HttpStatusCode.OK, await LeerAsync(Token()));

        using var nueva = RSA.Create(2048);
        Identity.Clave = nueva;
        var kidNuevo = SeguridadDePrueba.KidDe(nueva);
        var tokenConLaClaveNueva = Token(nueva, kidNuevo);

        // El módulo no conoce ese kid: vuelve a bajar el JWKS y reintenta con la clave nueva en la misma solicitud.
        Assert.Equal(HttpStatusCode.OK, await LeerAsync(tokenConLaClaveNueva));
        Assert.Equal(2, Identity.Llamadas);

        // Ya está en memoria: las siguientes no bajan nada más.
        Assert.Equal(HttpStatusCode.OK, await LeerAsync(tokenConLaClaveNueva));
        Assert.Equal(2, Identity.Llamadas);
    }

    [Fact]
    public async Task Quien_manda_tokens_con_kids_inventados_no_hace_que_el_modulo_bombardee_a_Identity()
    {
        Assert.Equal(HttpStatusCode.OK, await LeerAsync(Token()));
        using var atacante = RSA.Create(2048);

        var reloj = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 40; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, await LeerAsync(Token(atacante, $"kid-{i}")));
        }

        // La descarga inicial más una renovación por cada segundo que pasó (el mínimo entre renovaciones forzadas): nunca una
        // por cada token falso. El tope depende del tiempo para que una máquina lenta no lo rompa sin que el módulo falle.
        Assert.InRange(Identity.Llamadas, 1, 2 + (int)Math.Ceiling(reloj.Elapsed.TotalSeconds));
    }
}
