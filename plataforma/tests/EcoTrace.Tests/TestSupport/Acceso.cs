using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace EcoTrace.Tests.TestSupport;

/// <summary>
/// Datos de acceso de las pruebas. Son los de appsettings.Development.json de Identity (las pruebas corren
/// en Development), salvo la clave de firma, que se genera por corrida para no escribir nada en el repositorio.
/// </summary>
public static class Acceso
{
    public const string Emisor = "https://identity.ecotrace.local/desarrollo";
    public const string AdminEmail = "admin@ecotrace.local";
    public const string AdminPassword = "EcoTrace-Admin-Dev-2026";
    public const string SecretoBilling = "billing-dev-secreto-solo-local";
    public const string SecretoCargo = "cargo-dev-secreto-solo-local";
    public static readonly Guid OperadorTenantId = Guid.Parse("0e7ace00-0000-4000-8000-000000000001");

    private static readonly RSA ClaveDePrueba = RSA.Create(2048);

    /// <summary>Clave privada con la que firma Identity en las pruebas (se pasa por configuración, como en producción).</summary>
    public static string ClavePrivadaPem { get; } = ClaveDePrueba.ExportPkcs8PrivateKeyPem();

    /// <summary>Firma con la misma clave que Identity: sirve para forjar tokens "casi válidos" (vencidos, otro iss...).</summary>
    public static SigningCredentials CredencialesDeIdentity { get; } =
        new(new RsaSecurityKey(ClaveDePrueba.ExportParameters(true)), SecurityAlgorithms.RsaSha256);

    public static HttpClient ConToken(this HttpClient cliente, string token)
    {
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return cliente;
    }

    public static async Task<Respuesta> IniciarSesionAsync(this HttpClient identity, string email, string password) =>
        await identity.PostearAsync("/api/auth/login", new { email, password });

    public static async Task<string> TokenDeAsync(this HttpClient identity, string email, string password)
    {
        var sesion = await identity.IniciarSesionAsync(email, password);
        Assert.Equal(System.Net.HttpStatusCode.OK, sesion.Estado);
        return sesion.Texto("accessToken");
    }

    public static Task<string> TokenAdministradorAsync(this HttpClient identity) =>
        identity.TokenDeAsync(AdminEmail, AdminPassword);

    public static async Task<string> TokenServicioAsync(this HttpClient identity, string clientId, string secreto, string audiencia)
    {
        var respuesta = await identity.PostearAsync("/api/auth/token-servicio", new { clientId, clientSecret = secreto, audiencia });
        Assert.Equal(System.Net.HttpStatusCode.OK, respuesta.Estado);
        return respuesta.Texto("accessToken");
    }

    /// <summary>Cliente de Identity con token de administrador de la plataforma.</summary>
    public static async Task<HttpClient> ClienteAdministradorAsync(Func<HttpClient> crear)
    {
        var cliente = crear();
        return cliente.ConToken(await cliente.TokenAdministradorAsync());
    }

    /// <summary>Crea un usuario con contraseña (lo hace el administrador de la plataforma) y devuelve su id.</summary>
    public static async Task<Guid> CrearUsuarioAsync(
        this HttpClient admin, Guid tenantId, string rol, string email, string password)
    {
        var usuario = await admin.PostearAsync("/api/users", new { tenantId, role = rol, nombre = $"Usuario {rol}", email, password });
        Assert.Equal(System.Net.HttpStatusCode.Created, usuario.Estado);
        return usuario.Id("userId");
    }

    /// <summary>Firma un token con los claims dados. Por omisión, igual a uno de usuario válido de Identity.</summary>
    public static string Forjar(
        Guid? tenantId = null,
        string rol = "Administrador",
        string emisor = Emisor,
        string[]? audiencias = null,
        DateTime? expira = null,
        SigningCredentials? credenciales = null)
    {
        var ahora = DateTime.UtcNow;
        var vence = expira ?? ahora.AddMinutes(15);
        var emitido = vence < ahora ? vence.AddMinutes(-15) : ahora;
        var claims = new Dictionary<string, object>
        {
            ["sub"] = Guid.NewGuid().ToString(),
            ["role"] = rol,
            ["jti"] = Guid.NewGuid().ToString("N"),
            ["aud"] = audiencias ?? ["ecotrace-identity", "ecotrace-cargo"]
        };
        if (tenantId is not null)
        {
            claims["tenant_id"] = tenantId.Value.ToString();
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = emisor,
            IssuedAt = emitido,
            NotBefore = emitido,
            Expires = vence,
            Claims = claims,
            SigningCredentials = credenciales ?? CredencialesDeIdentity
        });
    }

    /// <summary>Lee el payload de un JWT sin validarlo (solo para inspeccionar claims en las pruebas).</summary>
    public static JsonElement Payload(string jwt)
    {
        var parte = jwt.Split('.')[1];
        return JsonSerializer.Deserialize<JsonElement>(Base64UrlEncoder.Decode(parte));
    }
}

/// <summary>
/// Lo que hará el escuadrón de Billing en su cliente HTTP: pedir un token de servicio a Identity, guardarlo
/// mientras sirva y mandarlo en cada llamada. En las pruebas el token se pide directo a Identity, sin pasar
/// por el cable que la prueba corta, para que los conteos de llamadas del Saga no cambien.
/// </summary>
public sealed class TokenDeServicioHandler(Func<HttpClient> identity, string clientId, string secreto, string audiencia)
    : DelegatingHandler
{
    private readonly SemaphoreSlim _candado = new(1, 1);
    private string? _token;
    private DateTime _renovarEn;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage solicitud, CancellationToken ct)
    {
        solicitud.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await ObtenerAsync(ct));
        return await base.SendAsync(solicitud, ct);
    }

    private async Task<string> ObtenerAsync(CancellationToken ct)
    {
        await _candado.WaitAsync(ct);
        try
        {
            if (_token is null || DateTime.UtcNow >= _renovarEn)
            {
                using var cliente = identity();
                var respuesta = await cliente.PostAsJsonAsync("/api/auth/token-servicio", new { clientId, clientSecret = secreto, audiencia }, ct);
                respuesta.EnsureSuccessStatusCode();
                var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
                _token = cuerpo.GetProperty("accessToken").GetString()!;
                _renovarEn = DateTime.UtcNow.AddMinutes(4);
            }

            return _token;
        }
        finally
        {
            _candado.Release();
        }
    }
}
