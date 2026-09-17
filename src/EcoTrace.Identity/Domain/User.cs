namespace EcoTrace.Identity.Domain;

// Cuenta de un usuario; pertenece a exactamente un Tenant (ADR 0001).
public class User
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public Role Role { get; set; }

    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }
}
