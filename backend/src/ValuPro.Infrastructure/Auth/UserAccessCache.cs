using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ValuPro.Domain.Security;
using ValuPro.Infrastructure.Identity;
using ValuPro.Infrastructure.Persistence;

namespace ValuPro.Infrastructure.Auth;

/// <summary>What a token is checked against on each request: is the user still allowed in, and with which permissions.</summary>
public sealed record UserAccess(
    Guid UserId,
    Guid TenantId,
    bool CanSignIn,
    string SecurityStamp,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);

/// <summary>
/// Caches each user's current access for a short time so tokens reflect role, permission and status
/// changes within <see cref="Lifetime"/> without a database hit per request. Changes made on this
/// server instance take effect immediately (entries are evicted); other instances catch up on expiry.
/// </summary>
public sealed class UserAccessCache(IMemoryCache cache)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    // Bumped when any role's permissions change, which invalidates every cached entry at once.
    private long _generation;

    private string Key(Guid userId) => $"access:{Interlocked.Read(ref _generation)}:{userId}";

    public async Task<UserAccess?> GetAsync(Guid userId, AppDbContext db, CancellationToken ct = default)
    {
        if (cache.TryGetValue(Key(userId), out UserAccess? cached)) return cached;

        var access = await LoadAsync(userId, db, ct);
        if (access is not null) cache.Set(Key(userId), access, Lifetime);
        return access;
    }

    public void Invalidate(Guid userId) => cache.Remove(Key(userId));

    public void InvalidateAll() => Interlocked.Increment(ref _generation);

    private static async Task<UserAccess?> LoadAsync(Guid userId, AppDbContext db, CancellationToken ct)
    {
        // Runs before the request's user (and so its tenant) is known: look the user up by its
        // globally unique id, then compare its tenant with the token's.
        var user = await db.Users.AsNoTracking()
            .IgnoreQueryFilters([AppDbContext.TenantFilter])
            .Where(u => u.Id == userId)
            .Select(u => new { u.Id, u.TenantId, u.Status, u.SecurityStamp })
            .FirstOrDefaultAsync(ct);
        if (user is null) return null;

        var roleIds = db.UserRoles.Where(ur => ur.UserId == userId).Select(ur => ur.RoleId);
        var roles = await db.Roles.Where(r => roleIds.Contains(r.Id)).Select(r => r.Name!).ToListAsync(ct);
        var permissions = await db.RoleClaims
            .Where(c => roleIds.Contains(c.RoleId) && c.ClaimType == Permissions.ClaimType)
            .Select(c => c.ClaimValue!)
            .Distinct()
            .OrderBy(p => p)
            .ToListAsync(ct);

        return new UserAccess(user.Id, user.TenantId, user.Status != UserStatus.Inactive, user.SecurityStamp ?? "", roles, permissions);
    }
}
