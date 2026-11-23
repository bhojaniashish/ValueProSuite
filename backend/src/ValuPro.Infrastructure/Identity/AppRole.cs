using Microsoft.AspNetCore.Identity;

namespace ValuPro.Infrastructure.Identity;

/// <summary>
/// Roles are shared across tenants for now; each role's permissions are role claims.
/// For SaaS, per-tenant permission overrides can be layered on top without renaming roles.
/// </summary>
public class AppRole : IdentityRole<Guid>
{
    public AppRole() => Id = Guid.CreateVersion7();
    public AppRole(string name) : this() { Name = name; }
}
