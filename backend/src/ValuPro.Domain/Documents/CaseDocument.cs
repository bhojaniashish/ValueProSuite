using ValuPro.Domain.Common;
using ValuPro.Domain.Properties;

namespace ValuPro.Domain.Documents;

/// <summary>The documents valuation reports list as "documents perused".</summary>
public enum DocumentType
{
    SaleDeed = 0,
    AgreementForSale = 1,
    IndexII = 2,
    SevenTwelve = 3,
    PropertyCard = 4,
    NaOrder = 5,
    ApprovedPlan = 6,
    BuildingPermission = 7,
    OccupancyCertificate = 8,
    TaxBill = 9,
    ElectricityBill = 10,
    ShareCertificate = 11,
    AllotmentLetter = 12,
    TitleReport = 13,
    EncumbranceCertificate = 14,
    Noc = 15,
    Other = 99,
}

/// <summary>What the valuer saw: the original, only a copy, or nothing (the bank's report says which were not produced).</summary>
public enum DocumentAvailability
{
    OriginalSeen = 0,
    CopyOnly = 1,
    NotProvided = 2,
}

/// <summary>A document recorded for an assignment. The file is optional: often only the number and date are noted.</summary>
public class CaseDocument : TenantEntity
{
    public Guid AssignmentId { get; set; }
    /// <summary>Copied from the assignment, so later cases on the same property can reuse the document.</summary>
    public Guid? PropertyId { get; set; }

    public DocumentType Type { get; set; }
    /// <summary>Name for "Other", or a more specific name ("Sale deed (2014, previous owner)").</summary>
    public string? Title { get; set; }
    public DocumentAvailability Availability { get; set; }
    public string? Number { get; set; }
    public DateOnly? IssuedOn { get; set; }
    /// <summary>Sub-registrar office, municipal corporation, talati…</summary>
    public string? IssuedBy { get; set; }
    public DateOnly? ValidUntil { get; set; }
    public string? Notes { get; set; }

    /// <summary>The case this document was first recorded on, when reused from an earlier case.</summary>
    public Guid? CopiedFromId { get; set; }

    public List<DocumentFile> Files { get; set; } = [];
}

/// <summary>One uploaded version of a document's file; the highest version is current, older ones are kept.</summary>
public class DocumentFile : TenantEntity
{
    public Guid DocumentId { get; set; }
    public int Version { get; set; }
    public required string BlobPath { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public required string UploadedByName { get; set; }
}

public static class DocumentRules
{
    /// <summary>Documents nearing their valid-until date (within this many days) are flagged.</summary>
    public const int ExpiringDays = 30;

    public static string Label(DocumentType t) => t switch
    {
        DocumentType.SaleDeed => "Sale deed",
        DocumentType.AgreementForSale => "Agreement for sale",
        DocumentType.IndexII => "Index II",
        DocumentType.SevenTwelve => "7/12 extract",
        DocumentType.PropertyCard => "Property card",
        DocumentType.NaOrder => "NA order",
        DocumentType.ApprovedPlan => "Approved plan",
        DocumentType.BuildingPermission => "Building permission",
        DocumentType.OccupancyCertificate => "Occupancy / BU certificate",
        DocumentType.TaxBill => "Property tax bill",
        DocumentType.ElectricityBill => "Electricity bill",
        DocumentType.ShareCertificate => "Share certificate",
        DocumentType.AllotmentLetter => "Allotment letter",
        DocumentType.TitleReport => "Title search report",
        DocumentType.EncumbranceCertificate => "Encumbrance certificate",
        DocumentType.Noc => "NOC",
        _ => "Other document",
    };

    /// <summary>
    /// The usual documents for a property type (what most banks list). Unknown type: a general set.
    /// A firm can record anything else on top; these just make the checklist.
    /// </summary>
    public static IReadOnlyList<DocumentType> Expected(PropertyType? type) => type switch
    {
        PropertyType.ResidentialFlat => [DocumentType.SaleDeed, DocumentType.IndexII, DocumentType.ApprovedPlan, DocumentType.OccupancyCertificate, DocumentType.TaxBill, DocumentType.ShareCertificate],
        PropertyType.CommercialShop or PropertyType.OfficeSpace =>
            [DocumentType.SaleDeed, DocumentType.IndexII, DocumentType.ApprovedPlan, DocumentType.OccupancyCertificate, DocumentType.TaxBill],
        PropertyType.IndependentHouse or PropertyType.Bungalow or PropertyType.RowHouse =>
            [DocumentType.SaleDeed, DocumentType.IndexII, DocumentType.PropertyCard, DocumentType.ApprovedPlan, DocumentType.OccupancyCertificate, DocumentType.TaxBill],
        PropertyType.OpenPlot => [DocumentType.SaleDeed, DocumentType.IndexII, DocumentType.SevenTwelve, DocumentType.NaOrder],
        PropertyType.AgriculturalLand => [DocumentType.SaleDeed, DocumentType.SevenTwelve, DocumentType.TitleReport],
        PropertyType.IndustrialShed or PropertyType.Warehouse =>
            [DocumentType.SaleDeed, DocumentType.IndexII, DocumentType.SevenTwelve, DocumentType.NaOrder, DocumentType.ApprovedPlan, DocumentType.OccupancyCertificate],
        _ => [DocumentType.SaleDeed, DocumentType.IndexII, DocumentType.ApprovedPlan, DocumentType.TaxBill],
    };

    /// <summary>Valid, expiring within <see cref="ExpiringDays"/>, or expired, given today's date in India.</summary>
    public static string? ExpiryState(DateOnly? validUntil, DateOnly today) =>
        validUntil is not { } d ? null : d < today ? "Expired" : d <= today.AddDays(ExpiringDays) ? "Expiring" : "Valid";
}
