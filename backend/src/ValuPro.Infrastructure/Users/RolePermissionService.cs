using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ValuPro.Application.Common;
using ValuPro.Application.Users;
using ValuPro.Domain.Security;
using ValuPro.Infrastructure.Auth;
using ValuPro.Infrastructure.Identity;
using ValuPro.Infrastructure.Persistence;

namespace ValuPro.Infrastructure.Users;

public sealed class RolePermissionService(
    AppDbContext db,
    RoleManager<AppRole> roles,
    UserAccessCache accessCache) : IRolePermissionService
{
    /// <summary>Admin always has every permission, so nobody can lock the firm out of User Management.</summary>
    private static readonly string[] Locked = [Roles.Admin];

    public async Task<PermissionMatrix> GetMatrixAsync(CancellationToken ct = default)
    {
        var claims = await db.RoleClaims
            .Where(c => c.ClaimType == Permissions.ClaimType)
            .Join(db.Roles, c => c.RoleId, r => r.Id, (c, r) => new { Role = r.Name!, Permission = c.ClaimValue! })
            .ToListAsync(ct);

        var grants = Roles.All.ToDictionary(
            r => r,
            r => (IReadOnlyList<string>)[.. claims.Where(c => c.Role == r).Select(c => c.Permission).Distinct().Order()]);

        return new PermissionMatrix(Roles.All, PermissionCatalog.Modules, grants, Locked);
    }

    public async Task<Result<PermissionMatrix>> UpdateRoleAsync(string roleName, IReadOnlyList<string> permissions,
        CancellationToken ct = default)
    {
        if (Locked.Contains(roleName)) return Error.Forbidden($"{roleName} always has full access.");

        var role = await roles.FindByNameAsync(roleName);
        if (role is null) return Error.NotFound("Role not found.");

        var unknown = permissions.Except(Permissions.All).ToList();
        if (unknown.Count > 0) return Error.Validation($"Unknown permission: {string.Join(", ", unknown)}.");

        var wanted = Normalize(permissions);
        var existing = (await roles.GetClaimsAsync(role)).Where(c => c.Type == Permissions.ClaimType).ToList();

        foreach (var claim in existing.Where(c => !wanted.Contains(c.Value)))
            await roles.RemoveClaimAsync(role, claim);
        foreach (var permission in wanted.Except(existing.Select(c => c.Value)))
            await roles.AddClaimAsync(role, new Claim(Permissions.ClaimType, permission));

        accessCache.InvalidateAll();
        return await GetMatrixAsync(ct);
    }

    /// <summary>Manage and Approve need View: you cannot edit what you cannot open.</summary>
    public static HashSet<string> Normalize(IEnumerable<string> permissions)
    {
        var set = new HashSet<string>(permissions, StringComparer.Ordinal);
        foreach (var module in PermissionCatalog.Modules)
        {
            var view = module.Actions[0].Key;
            if (module.Actions.Skip(1).Any(a => set.Contains(a.Key))) set.Add(view);
        }
        return set;
    }
}
