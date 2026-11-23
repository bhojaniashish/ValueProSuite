using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ValuPro.Domain.Security;
using ValuPro.Domain.Tenancy;
using ValuPro.Infrastructure.Identity;
using ValuPro.Infrastructure.Tenancy;

namespace ValuPro.Infrastructure.Persistence;

/// <summary>Creates the default firm, the seven roles with starting permissions, and the first admin.</summary>
public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var tenancy = sp.GetRequiredService<IOptions<TenancyOptions>>().Value;
        var config = sp.GetRequiredService<IConfiguration>();
        var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DbSeeder));

        if (!await db.Tenants.AnyAsync(t => t.Id == tenancy.DefaultTenantId, ct))
        {
            db.Tenants.Add(new Tenant
            {
                Id = tenancy.DefaultTenantId,
                Name = tenancy.DefaultTenantName,
                Slug = tenancy.DefaultTenantSlug,
            });
            await db.SaveChangesAsync(ct);
            log.LogInformation("Seeded default tenant {Tenant}", tenancy.DefaultTenantName);
        }

        // Roles get default permissions only when first created, so later edits in the matrix are kept.
        var roles = sp.GetRequiredService<RoleManager<AppRole>>();
        foreach (var roleName in Roles.All)
        {
            if (await roles.RoleExistsAsync(roleName)) continue;
            var role = new AppRole(roleName);
            Check(await roles.CreateAsync(role));
            foreach (var permission in Permissions.DefaultsFor(roleName))
                Check(await roles.AddClaimAsync(role, new Claim(Permissions.ClaimType, permission)));
        }

        // First admin: only when no user exists yet. Credentials come from configuration
        // (user-secrets locally, Key Vault in Azure) and must be changed after first login.
        var email = config["Seed:AdminEmail"];
        var password = config["Seed:AdminPassword"];
        var users = sp.GetRequiredService<UserManager<AppUser>>();
        if (!string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(password) && !await users.Users.AnyAsync(ct))
        {
            var admin = new AppUser
            {
                TenantId = tenancy.DefaultTenantId,
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FullName = config["Seed:AdminName"] ?? "Administrator",
                CreatedAt = DateTimeOffset.UtcNow,
            };
            Check(await users.CreateAsync(admin, password));
            Check(await users.AddToRoleAsync(admin, Roles.Admin));
            log.LogInformation("Seeded first admin user {Email}", email);
        }
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
    }
}
