using System.Reflection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ValuPro.Application.Abstractions;
using ValuPro.Domain.Common;
using ValuPro.Domain.Tenancy;
using ValuPro.Infrastructure.Identity;

namespace ValuPro.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenant)
    : IdentityDbContext<AppUser, AppRole, Guid>(options)
{
    public const string TenantFilter = "Tenant";
    public const string SoftDeleteFilter = "SoftDelete";

    /// <summary>Read per query, so every request sees only its own firm's rows.</summary>
    public Guid CurrentTenantId => tenant.TenantId;

    public DbSet<Tenant> Tenants => Set<Tenant>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Shorter table names than the AspNet* defaults.
        builder.Entity<AppUser>().ToTable("Users");
        builder.Entity<AppRole>().ToTable("Roles");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("UserRoles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("UserClaims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("UserLogins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("UserTokens");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("RoleClaims");

        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Every tenant-scoped / soft-deletable entity gets its filter automatically,
        // so new modules cannot forget it.
        foreach (var type in builder.Model.GetEntityTypes().Select(e => e.ClrType).ToList())
        {
            if (typeof(ITenantScoped).IsAssignableFrom(type))
            {
                Generic(nameof(ApplyTenantFilter), type).Invoke(this, [builder]);
                builder.Entity(type).HasIndex(nameof(ITenantScoped.TenantId));
            }
            if (typeof(ISoftDelete).IsAssignableFrom(type))
                Generic(nameof(ApplySoftDeleteFilter), type).Invoke(this, [builder]);
        }
    }

    private static MethodInfo Generic(string name, Type type) =>
        typeof(AppDbContext).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.MakeGenericMethod(type);

    private void ApplyTenantFilter<T>(ModelBuilder builder) where T : class, ITenantScoped =>
        builder.Entity<T>().HasQueryFilter(TenantFilter, e => e.TenantId == CurrentTenantId);

    private void ApplySoftDeleteFilter<T>(ModelBuilder builder) where T : class, ISoftDelete =>
        builder.Entity<T>().HasQueryFilter(SoftDeleteFilter, e => !e.IsDeleted);
}
