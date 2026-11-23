using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using ValuPro.Domain.Security;
using ValuPro.Infrastructure.Persistence;
using ValuPro.Infrastructure.Tenancy;

namespace ValuPro.Infrastructure.Auth;

/// <summary>
/// Runs after a token's signature and expiry are checked. Rejects tokens of deactivated users and
/// tokens issued before a password reset (security stamp), and replaces the token's roles and
/// permissions with the user's current ones, so matrix edits apply without signing in again.
/// </summary>
public static class TokenValidation
{
    public const string SecurityStampClaim = "sst";

    public static async Task OnTokenValidated(TokenValidatedContext context)
    {
        var principal = context.Principal;
        if (principal?.Identity is not ClaimsIdentity identity ||
            !Guid.TryParse(principal.FindFirst("sub")?.Value, out var userId))
        {
            context.Fail("Invalid token.");
            return;
        }

        var services = context.HttpContext.RequestServices;
        var access = await services.GetRequiredService<UserAccessCache>()
            .GetAsync(userId, services.GetRequiredService<AppDbContext>(), context.HttpContext.RequestAborted);

        if (access is null || !access.CanSignIn ||
            principal.FindFirst(SecurityStampClaim)?.Value != access.SecurityStamp ||
            principal.FindFirst(TenantContext.ClaimType)?.Value != access.TenantId.ToString())
        {
            context.Fail("Session is no longer valid.");
            return;
        }

        foreach (var claim in identity.FindAll(c => c.Type is Permissions.ClaimType or ClaimTypes.Role).ToList())
            identity.RemoveClaim(claim);
        identity.AddClaims(access.Roles.Select(r => new Claim(ClaimTypes.Role, r)));
        identity.AddClaims(access.Permissions.Select(p => new Claim(Permissions.ClaimType, p)));
    }
}
