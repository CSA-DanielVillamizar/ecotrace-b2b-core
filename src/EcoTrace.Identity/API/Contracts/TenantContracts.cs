using System.ComponentModel.DataAnnotations;
using EcoTrace.Identity.Domain;

namespace EcoTrace.Identity.Api.Contracts;

public record CreateTenantRequest([Required, MinLength(1)] string Name, TenantType TenantType);

public record TenantResponse(Guid Id, string Name, TenantType TenantType);
