using ValuPro.Domain.Common;

namespace ValuPro.Domain.Clients;

public enum ClientType
{
    Individual = 0,
    Builder = 1,
    BankBranch = 2,
    Advocate = 3,
}

/// <summary>Someone the firm values property for: an applicant, a builder, a bank branch or an advocate.</summary>
public class Client : TenantEntity
{
    /// <summary>Readable code shown everywhere, e.g. CL0007. Unique per firm, never reused.</summary>
    public required string Code { get; set; }

    public ClientType Type { get; set; }
    public required string Name { get; set; }
    public required string Mobile { get; set; }
    public string? Email { get; set; }
    public string? Gstin { get; set; }
    public string? Pan { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Pincode { get; set; }
    public string? Remarks { get; set; }
}
