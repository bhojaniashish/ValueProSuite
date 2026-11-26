using ValuPro.Domain.Common;

namespace ValuPro.Domain.Reports;

/// <summary>The parts of a case's report the valuer writes or chooses; everything else comes from the case's records.</summary>
public class CaseReport : TenantEntity
{
    public Guid AssignmentId { get; set; }
    /// <summary>Date printed on the report (default: the day it is generated).</summary>
    public DateOnly? ReportDate { get; set; }
    /// <summary>Date the value is stated as on (default: the inspection day).</summary>
    public DateOnly? ValuationDate { get; set; }
    /// <summary>The valuer's opinion / remarks section.</summary>
    public string? Opinion { get; set; }
    /// <summary>Null = the standard declaration.</summary>
    public string? Declaration { get; set; }
    public string? Place { get; set; }
    /// <summary>Photos per page in the photo annexure: 2 (large) or 4.</summary>
    public int PhotosPerPage { get; set; } = 4;
}

public enum ReportKind
{
    Draft = 0,
    Final = 1,
}

/// <summary>A generated PDF. Every generation is kept; the final one is what went to the bank.</summary>
public class ReportFile : TenantEntity
{
    public Guid AssignmentId { get; set; }
    public int Number { get; set; }
    public ReportKind Kind { get; set; }
    public required string BlobPath { get; set; }
    public long SizeBytes { get; set; }
    public required string GeneratedByName { get; set; }
    public decimal FairMarketValue { get; set; }

    public Guid? SignedByUserId { get; set; }
    public string? SignedByName { get; set; }
    /// <summary>Image now; ESign / Dsc later.</summary>
    public string? SignatureMethod { get; set; }
    public string? ApprovedByName { get; set; }
}

public enum EmailStatus
{
    Sent = 0,
    Failed = 1,
}

/// <summary>A report emailed to the bank (or a failed attempt), kept as the record of what went where.</summary>
public class ReportEmail : TenantEntity
{
    public Guid AssignmentId { get; set; }
    public Guid ReportFileId { get; set; }
    public int ReportNumber { get; set; }
    /// <summary>Comma-separated addresses.</summary>
    public required string To { get; set; }
    public string? Cc { get; set; }
    public required string Subject { get; set; }
    public Guid? SentByUserId { get; set; }
    public required string SentByName { get; set; }
    public EmailStatus Status { get; set; }
    public string? Error { get; set; }
}

public static class ReportText
{
    public static string DefaultDeclaration(string inspectedOn) =>
        "I hereby declare that:\n" +
        "a) the information furnished in this report is true and correct to the best of my knowledge and belief;\n" +
        "b) I have no direct or indirect interest in the property valued;\n" +
        $"c) I have personally inspected the property on {inspectedOn};\n" +
        "d) I have not been convicted of any offence and sentenced to a term of imprisonment;\n" +
        "e) I have not been found guilty of misconduct in my professional capacity;\n" +
        "f) the valuation has been made on the basis of the documents produced and the inspection carried out, and is valid as on the date of valuation.";
}
