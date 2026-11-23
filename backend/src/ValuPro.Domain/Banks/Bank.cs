using ValuPro.Domain.Common;

namespace ValuPro.Domain.Banks;

/// <summary>Which rates a bank wants the valuation based on.</summary>
public enum RateBasis
{
    MarketRates = 0,
    CircleRates = 1,
    BankRates = 2,
}

public enum DepreciationMethod
{
    StraightLine = 0,
    WrittenDownValue = 1,
}

public enum AnswerType
{
    YesNo = 0,
    Text = 1,
    Number = 2,
    Choice = 3,
}

/// <summary>A bank the firm is empanelled with, and how that bank wants valuations done.</summary>
public class Bank : TenantEntity
{
    public required string Name { get; set; }

    /// <summary>Short code shown on badges and in numbering, e.g. SBI. Unique per firm.</summary>
    public required string ShortCode { get; set; }

    public string BrandColor { get; set; } = "#2B50C4";

    /// <summary>First four letters of the bank's IFSC codes (e.g. SBIN), used to check branch IFSCs.</summary>
    public string? IfscPrefix { get; set; }

    /// <summary>The bank's report format name. Becomes a link to a report template in Phase 3.</summary>
    public string? ReportFormat { get; set; }

    public bool IsActive { get; set; } = true;

    // Valuation rules
    public RateBasis RateBasis { get; set; } = RateBasis.MarketRates;
    /// <summary>Distress (forced sale) value as a % of fair market value.</summary>
    public decimal DistressPercent { get; set; } = 80m;
    /// <summary>Realizable value as a % of fair market value.</summary>
    public decimal RealizablePercent { get; set; } = 90m;
    public DepreciationMethod Depreciation { get; set; } = DepreciationMethod.StraightLine;

    /// <summary>Bank-specific questions the inspector answers (dynamic questionnaire).</summary>
    public List<BankQuestion> Questions { get; set; } = [];

    public List<BankBranch> Branches { get; set; } = [];
}

public class BankQuestion
{
    public required string Text { get; set; }
    public AnswerType AnswerType { get; set; }
    /// <summary>For Choice questions: the allowed answers.</summary>
    public List<string> Options { get; set; } = [];
    public bool Required { get; set; }
}

public class BankBranch : TenantEntity
{
    public Guid BankId { get; set; }
    public Bank? Bank { get; set; }

    public required string Name { get; set; }
    public string? Ifsc { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Address { get; set; }
    public string? ContactPerson { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public bool IsActive { get; set; } = true;
}
