namespace EcoTrace.Identity.Domain;

// Empresa/organización dueña de los usuarios; otros módulos solo la referencian por Id (ADR 0001).
public class Tenant
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public TenantType TenantType { get; set; }

    public ICollection<User> Users { get; set; } = new List<User>();
}
