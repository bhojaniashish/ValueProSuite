using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ValuPro.Application.Abstractions;
using ValuPro.Domain.Common;

namespace ValuPro.Infrastructure.Persistence;

/// <summary>
/// On every save: stamps TenantId on new rows, fills audit fields, turns deletes of
/// soft-deletable rows into flags, and refuses to move a row to another tenant.
/// </summary>
public sealed class AuditingInterceptor(ICurrentUser user, ITenantContext tenant, TimeProvider clock)
    : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null) return;
        var now = clock.GetUtcNow();
        var userId = user.UserId;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is ITenantScoped scoped)
            {
                if (entry.State == EntityState.Added && scoped.TenantId == Guid.Empty)
                    scoped.TenantId = tenant.TenantId;
                else if (entry.State == EntityState.Modified)
                {
                    // Compare values, not IsModified: DbContext.Update() (used by ASP.NET Identity)
                    // flags every column as modified even when unchanged.
                    var tenantProperty = entry.Property(nameof(ITenantScoped.TenantId));
                    if (!Equals(tenantProperty.OriginalValue, tenantProperty.CurrentValue))
                        throw new InvalidOperationException("A record cannot be moved to another tenant.");
                }
            }

            if (entry.Entity is ISoftDelete soft && entry.State == EntityState.Deleted)
            {
                entry.State = EntityState.Modified;
                soft.IsDeleted = true;
                soft.DeletedAt = now;
            }

            if (entry.Entity is IAuditable audit)
            {
                if (entry.State == EntityState.Added)
                {
                    audit.CreatedAt = now;
                    audit.CreatedBy = userId;
                }
                else if (entry.State == EntityState.Modified)
                {
                    audit.UpdatedAt = now;
                    audit.UpdatedBy = userId;
                }
            }
        }
    }
}
