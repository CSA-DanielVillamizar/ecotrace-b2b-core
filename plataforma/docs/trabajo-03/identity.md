# Identity en el Trabajo 3: tokens y cómo validarlos

Lo que Identity ya entrega para el [Trabajo 3](especificacion.md) y lo que cada escuadrón necesita para usarlo. Identity es el Authorization Server (emite, renueva y revoca tokens y publica la clave pública) y también Resource Server de sí mismo: sus rutas exigen token.

## Rutas de autenticación

| Ruta | Cuerpo | Respuesta |
|---|---|---|
| `POST /api/auth/login` | `{ "email", "password" }` | `200 { accessToken, refreshToken, expiraEn }`. Si algo falla, `401` con el título genérico "Credenciales inválidas". `403` si la organización está suspendida (solo después de validar la contraseña). |
| `POST /api/auth/refresh` | `{ "refreshToken" }` | `200` con un access token nuevo y el mismo refresh token. `401` si está vencido, revocado o no existe. |
| `POST /api/auth/logout` | `{ "refreshToken" }` | `200`. Idempotente: uno ya revocado o desconocido también responde `200`. |
| `POST /api/auth/token-servicio` | `{ "clientId", "clientSecret", "audiencia" }` | `200 { accessToken, expiraEn }`. `401` con credenciales inválidas; `403` si el cliente no puede pedir esa audiencia. |
| `GET /api/auth/sesion` | (token) | Lo que Identity leyó del token: `sub`, `tenantId`, `role`, `aud`, `expiraEn`. Sirve para probar un token. |
| `GET /.well-known/jwks.json` | | La clave pública (`kty`, `alg`, `kid`, `n`, `e`). |

`expiraEn` es la fecha UTC en que vence el access token.

## El token

- **RS256**, con `kid` en el encabezado. La clave privada sale de `Jwt__ClavePrivadaPem` o `Jwt__ClavePrivadaArchivo`; en desarrollo, si no hay, Identity genera una en `App_Data` (ignorado por git). En cualquier otro entorno no arranca sin clave ni emisor.
- **Claims:** `sub` (UserId), `tenant_id`, `role`, `iss`, `aud`, `iat`, `nbf`, `exp` y `jti`.
- **`aud` de un usuario:** `["ecotrace-identity", "ecotrace-fleet", "ecotrace-cargo", "ecotrace-billing"]`. Un solo login sirve para los cuatro servicios.
- **Duración:** access token de 15 minutos, refresh token de 7 días (guardado como SHA-256, se revoca con logout), token de servicio de 5 minutos.
- **Emisor de desarrollo:** `https://identity.ecotrace.local/desarrollo`. La producción simulada (ADR 0006) tendrá otro emisor y otra clave.

## Token de servicio (para Billing y Cargo)

Las rutas internas de Identity (`/api/autorizaciones-pago`) ya exigen `role = Servicio`. Billing tiene que pedir un token antes de llamar a Identity en el Saga, o el paso 1 recibe `401`.

| `clientId` | Puede pedir `audiencia` | Secreto de desarrollo |
|---|---|---|
| `billing` | `ecotrace-identity`, `ecotrace-fleet` | `billing-dev-secreto-solo-local` |
| `cargo` | `ecotrace-billing` | `cargo-dev-secreto-solo-local` |

Identity solo guarda el SHA-256 de cada secreto (`Identity:ClientesServicio` en `appsettings.Development.json`). Los secretos de arriba son solo para desarrollo local; en producción cada servicio recibe el suyo por variable de entorno. Si otro servicio necesita un cliente, se agrega ahí.

El patrón en el cliente HTTP: pedir el token, guardarlo en memoria hasta un minuto antes de que venza, y mandarlo como `Authorization: Bearer ...` en cada llamada. El código está más abajo, en [Cómo llama un servicio a otro](#cómo-llama-un-servicio-a-otro-con-token-de-servicio).

## Cómo valida un Resource Server (Fleet, Cargo, Billing)

Con `Microsoft.AspNetCore.Authentication.JwtBearer` (ya está en `Directory.Packages.props`: en el `.csproj` de la Api basta `<PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" />`). Identity no publica un documento de discovery de OpenID, solo el JWKS, así que se le pasa al middleware un `ConfigurationManager` que lee ese JWKS:

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

var identityUrl = builder.Configuration["Servicios:Identity:BaseUrl"]!;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false;                   // conserva "role", "sub" y "tenant_id" con su nombre
    o.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
        $"{identityUrl}/.well-known/jwks.json",
        new JwksRetriever(),
        new HttpDocumentRetriever { RequireHttps = false });
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
        ValidIssuer = builder.Configuration["Jwt:Emisor"],      // de configuración, nunca constante
        ValidAudience = builder.Configuration["Jwt:Audiencia"], // "ecotrace-fleet", "ecotrace-cargo" o "ecotrace-billing"
        ClockSkew = TimeSpan.FromSeconds(30),
        RoleClaimType = "role",
        NameClaimType = "sub"
    };
});
builder.Services.AddAuthorization();
// ...
app.UseAuthentication();
app.UseAuthorization();

/// <summary>Convierte el JWKS de Identity en la configuración que espera JwtBearer.</summary>
public sealed class JwksRetriever : IConfigurationRetriever<OpenIdConnectConfiguration>
{
    public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(
        string address, IDocumentRetriever retriever, CancellationToken cancel)
    {
        var configuracion = new OpenIdConnectConfiguration();
        foreach (var clave in new JsonWebKeySet(await retriever.GetDocumentAsync(address, cancel)).GetSigningKeys())
        {
            configuracion.SigningKeys.Add(clave);
        }

        return configuracion;
    }
}
```

`ConfigurationManager` descarga el JWKS en la primera solicitud, lo guarda en memoria y lo vuelve a pedir solo si llega un `kid` que no conoce. Se comprobó contra Identity corriendo: token válido `200`, sin token o con la firma alterada `401`, token con `aud` de otro módulo `401`, rol sin permiso `403`, y **con Identity apagado un token válido sigue dando `200`**.

El `tenant_id` se lee del token (`User.FindFirst("tenant_id")`), nunca del cuerpo de la solicitud. Para el `401` genérico y el motivo en el registro, Identity lo hace en `OnChallenge` (ver `src/EcoTrace.Identity/Api/Seguridad/SeguridadExtensions.cs`).

## Cómo llama un servicio a otro con token de servicio

Un proveedor que pide el token a Identity y lo guarda hasta un minuto antes de que venza, y un `DelegatingHandler` que lo pone en cada llamada. Se usa un cliente aparte (`identity-auth`) para pedir el token, así el handler no se llama a sí mismo:

```csharp
builder.Services.AddHttpClient("identity-auth", c => c.BaseAddress = new Uri(identityUrl));
builder.Services.AddSingleton<ProveedorTokenServicio>();

// Ejemplo en Billing: el cliente que ya existe hacia Identity, ahora con su token.
builder.Services.AddHttpClient(ServiciosExternos.Identity, /* configuración actual */)
    .AddHttpMessageHandler(sp => new TokenDeServicioHandler(
        sp.GetRequiredService<ProveedorTokenServicio>(), "ecotrace-identity"));

public sealed class ProveedorTokenServicio(IHttpClientFactory fabrica, IConfiguration configuracion)
{
    private readonly SemaphoreSlim _candado = new(1, 1);
    private readonly Dictionary<string, (string Token, DateTime RenovarEn)> _cache = new();

    public async Task<string> ObtenerAsync(string audiencia, CancellationToken ct)
    {
        await _candado.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(audiencia, out var guardado) && DateTime.UtcNow < guardado.RenovarEn)
            {
                return guardado.Token;
            }

            var respuesta = await fabrica.CreateClient("identity-auth").PostAsJsonAsync("/api/auth/token-servicio", new
            {
                clientId = configuracion["Identity:ClientId"],
                clientSecret = configuracion["Identity:ClientSecret"],
                audiencia
            }, ct);
            respuesta.EnsureSuccessStatusCode();
            var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
            var token = cuerpo.GetProperty("accessToken").GetString()!;
            var expira = cuerpo.GetProperty("expiraEn").GetDateTime().ToUniversalTime();
            _cache[audiencia] = (token, expira.AddMinutes(-1));
            return token;
        }
        finally
        {
            _candado.Release();
        }
    }
}

public sealed class TokenDeServicioHandler(ProveedorTokenServicio proveedor, string audiencia) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage solicitud, CancellationToken ct)
    {
        solicitud.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await proveedor.ObtenerAsync(audiencia, ct));
        return await base.SendAsync(solicitud, ct);
    }
}
```

`Identity:ClientSecret` va en `appsettings.Development.json` del servicio (con el secreto de desarrollo de la tabla de arriba) y en producción por variable de entorno (`Identity__ClientSecret`). Si Identity no responde al pedir el token, la llamada falla como cualquier fallo de red y la resiliencia que ya tienen (reintentos del Saga, Outbox) la cubre.

## Quién puede llamar qué en Identity

| Ruta | Quién |
|---|---|
| `POST /api/tenants`, `POST /api/tenants/{id}/suspension` y `/reactivacion` | Administrador de la plataforma |
| `GET /api/tenants`, `GET /api/tenants/{id}` y `/estado` | Cualquier usuario de esa organización, un servicio o el administrador de la plataforma. La lista se filtra por el `tenant_id` del token. |
| `POST /api/users` | `Administrador`, en su propia organización (el de la plataforma, en cualquiera) |
| `GET /api/users`, `GET /api/users/{id}` | Usuarios de esa organización; el administrador de la plataforma ve todas. Un token de servicio recibe `403`. |
| `/api/autorizaciones-pago/*` | Solo `Servicio` |
| `/api/auth/*` (menos `sesion`), JWKS, `/api/roles`, `/health`, `/api/_meta/contexto` | Sin token |

Otra organización responde `403`; un token ausente, alterado, vencido o de otro emisor o audiencia, `401` con un cuerpo genérico (el motivo queda en el registro del servidor).

El **administrador de la plataforma** es el `Administrador` de la organización operadora (`TenantType = Operador`, id fijo `0e7ace00-0000-4000-8000-000000000001`). Identity la siembra al arrancar junto con su usuario inicial; en desarrollo es `admin@ecotrace.local` / `EcoTrace-Admin-Dev-2026`. La organización operadora no se puede crear por la API ni suspender.

Los usuarios aceptan ahora un campo opcional `password` al crearse (8 a 128 caracteres). Sin contraseña existen, pero no pueden iniciar sesión.

## Lo que cambia para lo que ya existía

- Las peticiones de [requests.http](../requests.http) piden primero el token del administrador.
- [trabajo-02/verificacion.http](../trabajo-02/verificacion.http) queda como evidencia del Trabajo 2: sus llamadas a Identity ahora necesitan `Authorization` (token de administrador para suspender y reactivar, token de servicio de `billing` para las autorizaciones de pago).
- La consola web todavía no envía token, así que sus formularios de Identity responden `401` hasta que se le agregue el login.
