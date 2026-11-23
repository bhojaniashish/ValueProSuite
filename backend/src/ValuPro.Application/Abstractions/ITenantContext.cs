namespace ValuPro.Application.Abstractions;

/// <summary>
/// The firm (tenant) the current request works for. Single-firm today: always the default tenant.
/// For SaaS, this resolves from the user's token or subdomain without other code changing.
/// </summary>
public interface ITenantContext
{
    Guid TenantId { get; }
}
