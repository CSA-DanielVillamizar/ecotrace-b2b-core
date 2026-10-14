namespace EcoTrace.Identity.Api.Seguridad;

/// <summary>Sección <c>Jwt</c>: cómo se firman y cuánto viven los tokens (ADR 0004).</summary>
public sealed class OpcionesJwt
{
    public const string Seccion = "Jwt";

    /// <summary>Emisor (<c>iss</c>). Es distinto en cada entorno: un token de desarrollo no sirve en producción.</summary>
    public string Emisor { get; set; } = string.Empty;

    /// <summary>Audiencias (<c>aud</c>) de un token de usuario: los módulos que la app consume con un solo login.</summary>
    public string[] Audiencias { get; set; } = [];

    /// <summary>Audiencia de este mismo servicio, con la que Identity valida los tokens que recibe.</summary>
    public string AudienciaPropia { get; set; } = "ecotrace-identity";

    public int MinutosAcceso { get; set; } = 15;

    public int DiasRefresco { get; set; } = 7;

    public int MinutosServicio { get; set; } = 5;

    /// <summary>Clave privada RSA en PEM. Sale de una variable de entorno (<c>Jwt__ClavePrivadaPem</c>), nunca del repositorio.</summary>
    public string? ClavePrivadaPem { get; set; }

    /// <summary>Alternativa a <see cref="ClavePrivadaPem"/>: ruta a un archivo PEM montado como secreto.</summary>
    public string? ClavePrivadaArchivo { get; set; }

    /// <summary>
    /// Solo para desarrollo: si no hay clave configurada, genera una y la guarda en App_Data (que está en
    /// .gitignore). En producción debe quedar en false para que el servicio no arranque sin clave.
    /// </summary>
    public bool GenerarClaveSiFalta { get; set; }
}

/// <summary>Sección <c>Identity:Operador</c>: la organización que opera la plataforma y su primer administrador.</summary>
public sealed class OpcionesOperador
{
    public const string Seccion = "Identity:Operador";

    public Guid TenantId { get; set; }

    public string Nombre { get; set; } = "EcoTrace Operaciones";

    /// <summary>Correo del administrador inicial. Si falta, no se siembra ningún usuario.</summary>
    public string? AdminEmail { get; set; }

    /// <summary>Contraseña del administrador inicial. En producción sale de una variable de entorno.</summary>
    public string? AdminPassword { get; set; }
}

/// <summary>Un servicio que puede pedir token de servicio (<c>POST /api/auth/token-servicio</c>).</summary>
public sealed class ClienteServicio
{
    public const string Seccion = "Identity:ClientesServicio";

    public string ClientId { get; set; } = string.Empty;

    /// <summary>SHA-256 del secreto, en hexadecimal. Identity nunca guarda el secreto en claro.</summary>
    public string SecretoSha256 { get; set; } = string.Empty;

    /// <summary>Módulos a los que este servicio puede llamar (valores posibles de <c>aud</c>).</summary>
    public string[] Audiencias { get; set; } = [];
}
