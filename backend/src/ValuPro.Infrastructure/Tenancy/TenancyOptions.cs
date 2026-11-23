namespace ValuPro.Infrastructure.Tenancy;

public sealed class TenancyOptions
{
    public const string Section = "Tenancy";

    /// <summary>The single firm used until SaaS sign-up exists.</summary>
    public Guid DefaultTenantId { get; set; }
    public string DefaultTenantName { get; set; } = "ValuPro";
    public string DefaultTenantSlug { get; set; } = "default";
}
