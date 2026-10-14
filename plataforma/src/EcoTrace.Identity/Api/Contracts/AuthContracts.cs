namespace EcoTrace.Identity.Api.Contracts;

// Contrato de autenticación del Trabajo 3 (docs/trabajo-03/especificacion.md, "Identity: emitir y revocar").
// Los campos no llevan [Required] a propósito: un login incompleto es un 401 genérico, no un 400 que
// diga qué campo faltó.

public sealed record LoginRequest(string? Email, string? Password);

public sealed record RefrescoRequest(string? RefreshToken);

public sealed record TokenServicioRequest(string? ClientId, string? ClientSecret, string? Audiencia);

/// <summary>Sesión emitida. <c>ExpiraEn</c> es el vencimiento (UTC) del access token.</summary>
public sealed record SesionResponse(string AccessToken, string RefreshToken, DateTime ExpiraEn);

public sealed record TokenServicioResponse(string AccessToken, DateTime ExpiraEn);

/// <summary>Lo que Identity leyó del token recibido. Sirve para que la app y los demás escuadrones prueben su token.</summary>
public sealed record SesionActualResponse(string Sub, Guid? TenantId, string Role, string[] Aud, DateTime ExpiraEn);
