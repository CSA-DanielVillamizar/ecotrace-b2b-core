using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace EcoTrace.Identity.Api.Seguridad;

/// <summary>
/// La clave RSA con la que Identity firma (RS256) y su parte pública, que se publica en el JWKS.
/// La privada sale de configuración o de un archivo montado; nunca del repositorio (ADR 0004).
/// No es IDisposable a propósito: vive lo que vive el proceso, y Microsoft.IdentityModel guarda en caché
/// el proveedor de firma por kid; liberar el RSA dejaría esa caché apuntando a un objeto muerto.
/// </summary>
public sealed class ClavesDeFirma
{
    private const string ArchivoDesarrollo = "jwt-desarrollo.pem";

    private ClavesDeFirma(RSA rsa)
    {
        if (rsa.KeySize < 2048)
        {
            throw new InvalidOperationException("La clave de firma debe tener al menos 2048 bits.");
        }

        var parametros = rsa.ExportParameters(includePrivateParameters: false);
        ClavePublica = new RsaSecurityKey(parametros);
        Kid = Base64UrlEncoder.Encode(JsonWebKeyConverter.ConvertFromRSASecurityKey(ClavePublica).ComputeJwkThumbprint());
        ClavePublica.KeyId = Kid;

        // Fábrica propia: la caché de proveedores no se comparte con otra instancia que tenga el mismo kid.
        var clavePrivada = new RsaSecurityKey(rsa) { KeyId = Kid, CryptoProviderFactory = new CryptoProviderFactory() };
        Credenciales = new SigningCredentials(clavePrivada, SecurityAlgorithms.RsaSha256);
        Jwks = new
        {
            keys = new[]
            {
                new
                {
                    kty = "RSA",
                    use = "sig",
                    alg = SecurityAlgorithms.RsaSha256,
                    kid = Kid,
                    n = Base64UrlEncoder.Encode(parametros.Modulus),
                    e = Base64UrlEncoder.Encode(parametros.Exponent)
                }
            }
        };
    }

    /// <summary>Identificador de la clave (huella RFC 7638). Va en el encabezado de cada token y en el JWKS.</summary>
    public string Kid { get; }

    public RsaSecurityKey ClavePublica { get; }

    public SigningCredentials Credenciales { get; }

    /// <summary>Documento JWKS: solo la parte pública.</summary>
    public object Jwks { get; }

    public static ClavesDeFirma Cargar(OpcionesJwt opciones, string contentRoot)
    {
        var pem = opciones.ClavePrivadaPem;
        if (string.IsNullOrWhiteSpace(pem) && !string.IsNullOrWhiteSpace(opciones.ClavePrivadaArchivo))
        {
            pem = File.ReadAllText(opciones.ClavePrivadaArchivo);
        }

        if (string.IsNullOrWhiteSpace(pem))
        {
            if (!opciones.GenerarClaveSiFalta)
            {
                throw new InvalidOperationException(
                    "No hay clave de firma. Configure Jwt__ClavePrivadaPem o Jwt__ClavePrivadaArchivo.");
            }

            pem = ObtenerClaveDeDesarrollo(Path.Combine(contentRoot, "App_Data", ArchivoDesarrollo));
        }

        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return new ClavesDeFirma(rsa);
    }

    private static string ObtenerClaveDeDesarrollo(string ruta)
    {
        if (File.Exists(ruta))
        {
            return File.ReadAllText(ruta);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        using var nueva = RSA.Create(2048);
        var pem = nueva.ExportPkcs8PrivateKeyPem();
        try
        {
            using var archivo = new FileStream(ruta, FileMode.CreateNew, FileAccess.Write);
            using var escritor = new StreamWriter(archivo);
            escritor.Write(pem);
            return pem;
        }
        catch (IOException) when (File.Exists(ruta))
        {
            // Otra instancia la creó primero: se usa esa para que ambas firmen con la misma clave.
            return File.ReadAllText(ruta);
        }
    }
}
