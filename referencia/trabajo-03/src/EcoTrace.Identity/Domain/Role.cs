namespace EcoTrace.Identity.Domain;

/// <summary>
/// Rol de un usuario (Conductor, Supervisor, Administrador, Auditor). Sus permisos viven
/// como <see cref="RoleClaim"/> y viajaran en el JWT cuando se implemente el Trabajo 3.
/// </summary>
public sealed class Role
{
    private Role()
    {
        Nombre = string.Empty;
    }

    public Role(int roleId, string nombre)
    {
        RoleId = roleId;
        Nombre = nombre;
    }

    public int RoleId { get; private set; }

    public string Nombre { get; private set; }

    public List<RoleClaim> Claims { get; private set; } = [];
}

public sealed class RoleClaim
{
    private RoleClaim()
    {
        Tipo = string.Empty;
        Valor = string.Empty;
    }

    public RoleClaim(int roleClaimId, int roleId, string tipo, string valor)
    {
        RoleClaimId = roleClaimId;
        RoleId = roleId;
        Tipo = tipo;
        Valor = valor;
    }

    public int RoleClaimId { get; private set; }

    public int RoleId { get; private set; }

    public string Tipo { get; private set; }

    public string Valor { get; private set; }
}
