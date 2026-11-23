namespace ValuPro.Domain.Common;

/// <summary>Rows owned by one firm (tenant). Queries are filtered by the current tenant automatically.</summary>
public interface ITenantScoped
{
    Guid TenantId { get; set; }
}

public interface IAuditable
{
    DateTimeOffset CreatedAt { get; set; }
    Guid? CreatedBy { get; set; }
    DateTimeOffset? UpdatedAt { get; set; }
    Guid? UpdatedBy { get; set; }
}

/// <summary>Rows are flagged as deleted instead of being removed, and hidden from queries.</summary>
public interface ISoftDelete
{
    bool IsDeleted { get; set; }
    DateTimeOffset? DeletedAt { get; set; }
}
