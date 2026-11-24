using FluentValidation;
using ValuPro.Application.Common;
using ValuPro.Domain.Assignments;
using ValuPro.Domain.Documents;
using ValuPro.Domain.Properties;

namespace ValuPro.Application.Documents;

public sealed record DocumentFileItem(Guid Id, int Version, string FileName, string ContentType, long SizeBytes, DateTimeOffset UploadedAt, string UploadedByName);

public sealed record DocumentItem(
    Guid Id,
    Guid AssignmentId,
    DocumentType Type,
    string Label,
    string? Title,
    DocumentAvailability Availability,
    string? Number,
    DateOnly? IssuedOn,
    string? IssuedBy,
    DateOnly? ValidUntil,
    /// <summary>Valid, Expiring or Expired; null without a valid-until date.</summary>
    string? ExpiryState,
    string? Notes,
    /// <summary>All file versions, newest first; the first is the current one.</summary>
    IReadOnlyList<DocumentFileItem> Files,
    string? CopiedFromCase,
    DateTimeOffset CreatedAt);

/// <summary>One line of the checklist: a document type the property usually needs and whether it is covered.</summary>
public sealed record ChecklistItem(DocumentType Type, string Label, bool Recorded, bool NotProvided);

/// <summary>A document from an earlier case on the same property that can be reused.</summary>
public sealed record EarlierDocument(DocumentItem Document, string CaseNumber);

public sealed record CaseDocuments(
    Guid AssignmentId,
    string Number,
    AssignmentStatus Status,
    string BankShortCode,
    string BankColor,
    string ApplicantName,
    string PropertySummary,
    PropertyType? PropertyType,
    IReadOnlyList<DocumentItem> Documents,
    IReadOnlyList<ChecklistItem> Checklist,
    IReadOnlyList<EarlierDocument> Earlier,
    bool CanEdit,
    string? ReadOnlyReason);

/// <summary>Firm-wide list. Expiry: "Expiring" or "Expired".</summary>
public sealed record DocumentQuery(
    string? Search = null,
    DocumentType? Type = null,
    string? Expiry = null,
    bool Mine = false,
    int Page = 1,
    int PageSize = 25);

public sealed record DocumentListItem(DocumentItem Document, string CaseNumber, string BankShortCode, string BankColor, string ApplicantName);

public sealed record DocumentSummary(int Total, int WithFile, int NotProvided, int Expiring, int Expired);

public sealed record DocumentPage(PagedResult<DocumentListItem> Page, DocumentSummary Summary);

public sealed record SaveDocumentRequest(
    DocumentType Type,
    string? Title,
    DocumentAvailability Availability,
    string? Number,
    DateOnly? IssuedOn,
    string? IssuedBy,
    DateOnly? ValidUntil,
    string? Notes);

public sealed record DownloadFile(Stream Content, string ContentType, string FileName);

public interface IDocumentService
{
    Task<DocumentPage> ListAsync(DocumentQuery query, CancellationToken ct = default);
    Task<Result<CaseDocuments>> GetCaseAsync(Guid assignmentId, CancellationToken ct = default);
    Task<Result<DocumentItem>> CreateAsync(Guid assignmentId, SaveDocumentRequest request, CancellationToken ct = default);
    Task<Result<DocumentItem>> UpdateAsync(Guid documentId, SaveDocumentRequest request, CancellationToken ct = default);
    Task<Result<bool>> DeleteAsync(Guid documentId, CancellationToken ct = default);
    /// <summary>Adds a file as the document's newest version; earlier versions are kept.</summary>
    Task<Result<DocumentItem>> UploadAsync(Guid documentId, Stream content, string fileName, string contentType, long size, CancellationToken ct = default);
    /// <summary>Records a document from an earlier case on the same property for this case (same files, no re-upload).</summary>
    Task<Result<DocumentItem>> ReuseAsync(Guid assignmentId, Guid sourceDocumentId, CancellationToken ct = default);
    Task<Result<DownloadFile>> OpenFileAsync(Guid fileId, CancellationToken ct = default);
    Task<Result<DownloadFile>> ZipAsync(Guid assignmentId, CancellationToken ct = default);
}

public sealed class SaveDocumentRequestValidator : AbstractValidator<SaveDocumentRequest>
{
    public SaveDocumentRequestValidator(TimeProvider clock)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(TimeSpan.FromHours(5.5)).DateTime);
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Availability).IsInEnum();
        RuleFor(x => x.Title).NotEmpty().When(x => x.Type == DocumentType.Other).WithMessage("Name the document.");
        RuleFor(x => x.Title).MaximumLength(120);
        RuleFor(x => x.Number).MaximumLength(80);
        RuleFor(x => x.IssuedBy).MaximumLength(120);
        RuleFor(x => x.Notes).MaximumLength(1000);
        RuleFor(x => x.IssuedOn).LessThanOrEqualTo(today).When(x => x.IssuedOn is not null).WithMessage("The date can't be in the future.");
        RuleFor(x => x.ValidUntil).GreaterThanOrEqualTo(x => x.IssuedOn).When(x => x.ValidUntil is not null && x.IssuedOn is not null)
            .WithMessage("Valid-until is before the issue date.");
        RuleFor(x => x.Notes).NotEmpty().When(x => x.Availability == DocumentAvailability.NotProvided)
            .WithMessage("Say why it was not provided (e.g. \"With the bank\", \"Lost, applying for a copy\").");
    }
}

public sealed class DocumentQueryValidator : AbstractValidator<DocumentQuery>
{
    public DocumentQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
        RuleFor(x => x.Expiry).Must(e => e is null or "Expiring" or "Expired").WithMessage("Expiry must be Expiring or Expired.");
    }
}
