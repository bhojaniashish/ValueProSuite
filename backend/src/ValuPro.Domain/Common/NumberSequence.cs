namespace ValuPro.Domain.Common;

/// <summary>
/// A per-firm counter for readable numbers (client codes now; assignment and invoice numbers later).
/// Numbers are never reused, even when a record is deleted.
/// </summary>
public class NumberSequence : Entity, ITenantScoped
{
    public Guid TenantId { get; set; }

    /// <summary>What is being numbered, e.g. "client" or "assignment:2026".</summary>
    public required string Key { get; set; }

    /// <summary>The last number handed out. Also the concurrency token, so two saves cannot take the same number.</summary>
    public long Last { get; set; }
}
