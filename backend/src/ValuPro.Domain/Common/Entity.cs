namespace ValuPro.Domain.Common;

/// <summary>Base type for every persisted entity.</summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
}

/// <summary>Entity with created/updated audit stamps, filled automatically on save.</summary>
public abstract class AuditableEntity : Entity, IAuditable
{
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

/// <summary>
/// Base type for all business data (clients, assignments, reports…).
/// Carries TenantId from day one so the app can become multi-tenant SaaS without a data migration.
/// </summary>
public abstract class TenantEntity : AuditableEntity, ITenantScoped, ISoftDelete
{
    public Guid TenantId { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
