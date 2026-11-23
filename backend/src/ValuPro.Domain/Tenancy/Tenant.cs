using ValuPro.Domain.Common;

namespace ValuPro.Domain.Tenancy;

/// <summary>A valuation firm. Today there is one; later each SaaS customer is a tenant.</summary>
public class Tenant : AuditableEntity
{
    public required string Name { get; set; }

    /// <summary>URL-safe short name, used later for subdomains and blob paths.</summary>
    public required string Slug { get; set; }

    public string? RegisteredValuerNo { get; set; }
    public string? IbbiRegistration { get; set; }
    public string? ContactEmail { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? State { get; set; }
    public string? LogoBlobPath { get; set; }
    public string? WatermarkBlobPath { get; set; }

    /// <summary>Changes whenever branding changes, so browsers re-fetch the logo (cache busting).</summary>
    public int BrandingVersion { get; set; }
    public string PrimaryColor { get; set; } = "#2B50C4";
    public string AccentColor { get; set; } = "#C2922E";
    public bool IsActive { get; set; } = true;
}
