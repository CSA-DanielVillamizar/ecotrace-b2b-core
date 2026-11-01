using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using EcoTrace.Identity.Api.Security;
using EcoTrace.Identity.Domain;
using EcoTrace.Identity.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EcoTrace.Identity.Api.Controllers;

public sealed record LoginRequest
{
    [Required]
    public string? Email { get; init; }

    [Required]
    public string? Password { get; init; }
}

public sealed record TokenResponse(string AccessToken, string RefreshToken, DateTime ExpiraEn, string Tipo = "Bearer");

public sealed record RefreshRequest
{
    [Required]
    public string? RefreshToken { get; init; }
}

public sealed record TokenServicioRequest
{
    [Required]
    public string? ClientId { get; init; }

    [Required]
    public string? ClientSecret { get; init; }

    /// <summary>Módulo de destino: ecotrace-fleet, ecotrace-cargo, ecotrace-billing o ecotrace-identity.</summary>
    [Required]
    public string? Audiencia { get; init; }
}

public sealed record TokenServicioResponse(string AccessToken, DateTime ExpiraEn, string Tipo = "Bearer");

/// <summary>
/// Identity como Authorization Server (ADR 0004): es el único que emite tokens. Todas estas rutas son anónimas
/// porque es lo que se usa para conseguir un token.
/// </summary>
[ApiController]
[AllowAnonymous]
[Produces("application/json")]
public sealed class AuthController(
    IdentityDbContext db, EmisorDeTokens emisor, ClavesDeFirma claves, TimeProvider reloj,
    IOptions<OpcionesJwt> opcionesJwt, IOptions<List<ClienteDeServicio>> clientes,
    ILogger<AuthController> registro) : ControllerBase
{
    // Para que "el correo no existe" tarde lo mismo que "la contraseña no coincide": sin esto, medir el tiempo de
    // respuesta delata qué cuentas existen.
    private static readonly Lazy<string> HashFicticio = new(() => Contrasenas.Hashear("no-es-una-contrasena-real", 1_000));

    /// <summary>Inicia sesión con correo y contraseña. Si falla, el mensaje es el mismo sin decir qué campo falló.</summary>
    [HttpPost("api/auth/login")]
    [ProducesResponseType<TokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(LoginRequest solicitud, CancellationToken ct)
    {
        var email = solicitud.Email!.Trim().ToLowerInvariant();
        var usuario = await db.Users.Include(u => u.Role).Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.Email == email, ct);

        var coincide = Contrasenas.Verificar(solicitud.Password!, usuario?.PasswordHash ?? HashFicticio.Value);
        if (usuario is null || !coincide || usuario.Tenant?.Estado != EstadoTenant.Activo)
        {
            // El motivo exacto (cuenta inexistente, contraseña, organización suspendida) va al registro, nunca al cliente.
            registro.LogWarning("Inicio de sesión rechazado ({Motivo})",
                usuario is null ? "cuenta inexistente" : !coincide ? "contraseña" : "organización suspendida");
            return CredencialesInvalidas();
        }

        var ahora = reloj.GetUtcNow().UtcDateTime;
        var refresco = EmisorDeTokens.NuevoRefreshToken();
        db.RefreshTokens.Add(RefreshToken.Emitir(
            usuario.UserId, EmisorDeTokens.HashDe(refresco), ahora, TimeSpan.FromDays(opcionesJwt.Value.RefreshTokenDias)));
        await db.SaveChangesAsync(ct);

        var acceso = emisor.EmitirAcceso(usuario, usuario.Role!.Nombre, usuario.Tenant!.TenantType == TenantType.Plataforma);
        return Ok(new TokenResponse(acceso.Token, refresco, acceso.ExpiraEnUtc));
    }

    /// <summary>Cambia un refresh token vigente por un access token nuevo.</summary>
    [HttpPost("api/auth/refresh")]
    [ProducesResponseType<TokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refrescar(RefreshRequest solicitud, CancellationToken ct)
    {
        var hash = EmisorDeTokens.HashDe(solicitud.RefreshToken!);
        var token = await db.RefreshTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        var ahora = reloj.GetUtcNow().UtcDateTime;

        if (token is null || !token.EstaVigente(ahora))
        {
            registro.LogWarning("Refresh rechazado ({Motivo})", token is null ? "no existe" : "vencido o revocado");
            return SesionInvalida();
        }

        var usuario = await db.Users.AsNoTracking().Include(u => u.Role).Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.UserId == token.UserId, ct);

        // Una organización suspendida después de iniciar sesión deja de poder renovar.
        if (usuario?.Tenant?.Estado != EstadoTenant.Activo)
        {
            registro.LogWarning("Refresh rechazado (organización suspendida o usuario inexistente)");
            return SesionInvalida();
        }

        var acceso = emisor.EmitirAcceso(usuario, usuario.Role!.Nombre, usuario.Tenant!.TenantType == TenantType.Plataforma);
        return Ok(new TokenResponse(acceso.Token, solicitud.RefreshToken!, acceso.ExpiraEnUtc));
    }

    /// <summary>Revoca el refresh token. Es idempotente: revocar uno ya revocado, o uno que no existe, responde 200.</summary>
    [HttpPost("api/auth/logout")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> CerrarSesion(RefreshRequest solicitud, CancellationToken ct)
    {
        var hash = EmisorDeTokens.HashDe(solicitud.RefreshToken!);
        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token?.Revocar(reloj.GetUtcNow().UtcDateTime) == true)
        {
            await db.SaveChangesAsync(ct);
        }

        return Ok(new { cerrada = true });
    }

    /// <summary>Token para llamadas entre servicios: rol Servicio, un solo módulo de destino y 5 minutos.</summary>
    [HttpPost("api/auth/token-servicio")]
    [ProducesResponseType<TokenServicioResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult TokenDeServicio(TokenServicioRequest solicitud)
    {
        var cliente = clientes.Value.FirstOrDefault(c => c.ClientId == solicitud.ClientId);

        // Se compara siempre, aunque el cliente no exista, y en tiempo constante.
        var secretoEsperado = cliente?.ClientSecret ?? Guid.NewGuid().ToString("N");
        var secretoValido = CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(secretoEsperado)),
            SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(solicitud.ClientSecret!)));

        if (cliente is null || !secretoValido || !cliente.Audiencias.Contains(solicitud.Audiencia))
        {
            registro.LogWarning("Token de servicio rechazado para el cliente {Cliente}", solicitud.ClientId);
            return CredencialesInvalidas();
        }

        var token = emisor.EmitirServicio(cliente.ClientId, solicitud.Audiencia!);
        return Ok(new TokenServicioResponse(token.Token, token.ExpiraEnUtc));
    }

    /// <summary>La clave pública con la que Identity firma (JWKS, RFC 7517). Los demás módulos validan con ella.</summary>
    [HttpGet("/.well-known/jwks.json")]
    [Produces("application/json")]
    public ContentResult Jwks() => Content(claves.Jwks(), "application/json");

    private ObjectResult CredencialesInvalidas() => Problem(
        statusCode: StatusCodes.Status401Unauthorized, title: "Credenciales inválidas",
        detail: "Credenciales inválidas.");

    private ObjectResult SesionInvalida() => Problem(
        statusCode: StatusCodes.Status401Unauthorized, title: "Sesión inválida",
        detail: "La sesión venció o no existe. Inicia sesión de nuevo.");
}
