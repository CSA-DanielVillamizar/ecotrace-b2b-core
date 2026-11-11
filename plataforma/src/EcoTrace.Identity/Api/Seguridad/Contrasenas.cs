using Microsoft.AspNetCore.Identity;
using Usuario = EcoTrace.Identity.Domain.User;

namespace EcoTrace.Identity.Api.Seguridad;

/// <summary>Hash y verificación de contraseñas con el PasswordHasher de ASP.NET (PBKDF2).</summary>
public sealed class Contrasenas(IPasswordHasher<Usuario> hasher)
{
    // Se verifica contra este hash cuando el correo no existe, para que la respuesta tarde lo mismo
    // y no delate qué correos están registrados.
    private readonly Lazy<string> _senuelo = new(() => hasher.HashPassword(null!, Guid.NewGuid().ToString()));

    public string Calcular(Usuario usuario, string contrasena) => hasher.HashPassword(usuario, contrasena);

    public ResultadoVerificacion Verificar(Usuario? usuario, string? contrasena)
    {
        if (usuario?.ContrasenaHash is null || string.IsNullOrEmpty(contrasena))
        {
            hasher.VerifyHashedPassword(null!, _senuelo.Value, contrasena ?? string.Empty);
            return ResultadoVerificacion.Invalida;
        }

        return hasher.VerifyHashedPassword(usuario, usuario.ContrasenaHash, contrasena) switch
        {
            PasswordVerificationResult.Success => ResultadoVerificacion.Valida,
            PasswordVerificationResult.SuccessRehashNeeded => ResultadoVerificacion.ValidaConRehash,
            _ => ResultadoVerificacion.Invalida
        };
    }
}

public enum ResultadoVerificacion
{
    Invalida,
    Valida,
    ValidaConRehash
}
