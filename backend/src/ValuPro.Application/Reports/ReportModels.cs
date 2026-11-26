using FluentValidation;
using ValuPro.Application.Common;
using ValuPro.Domain.Assignments;
using ValuPro.Domain.Documents;
using ValuPro.Domain.Inspections;
using ValuPro.Domain.Properties;
using ValuPro.Domain.Reports;
using ValuPro.Domain.Valuations;

namespace ValuPro.Application.Reports;

/// <summary>
/// Report stages for the list: "draft" (valued, report not yet in review), "review" (Quality Review),
/// "done" (Submitted or invoiced).
/// </summary>
public sealed record ReportQuery(string Stage = "draft", bool Mine = true, string? Search = null);

public sealed record ReportListItem(
    Guid AssignmentId, string Number, AssignmentStatus Status, string BankShortCode, string BankColor, string ApplicantName,
    string PropertySummary, string ValuerName, string? ReviewerName, DateOnly DueDate, decimal? FairMarketValue, int Drafts, DateTimeOffset? LastGeneratedAt);

/// <summary>One readiness check. Blocking checks stop the report; the rest are reminders.</summary>
public sealed record ReportCheck(string Key, string Label, bool Ok, bool Blocking, string? Detail);

public sealed record ReportFileItem(Guid Id, int Number, ReportKind Kind, long SizeBytes, DateTimeOffset GeneratedAt, string GeneratedByName,
    decimal FairMarketValue, string? SignedByName, string? ApprovedByName);

public sealed record ReportFields(DateOnly? ReportDate, DateOnly? ValuationDate, string? Opinion, string Declaration, bool DeclarationIsStandard,
    string? Place, int PhotosPerPage);

public sealed record ReportDetail(
    Guid AssignmentId, string Number, AssignmentStatus Status, string BankShortCode, string BankColor, string ApplicantName,
    string PropertySummary, string ValuerName, string? ReviewerName, decimal? FairMarketValue,
    IReadOnlyList<ReportCheck> Checks, ReportFields Fields, IReadOnlyList<ReportFileItem> Files,
    bool CanEdit, bool CanGenerate, string? GenerateBlockedReason, bool CanApprove, string? ApproveBlockedReason, string? ReadOnlyReason,
    CaseType CaseType = CaseType.Valuation, decimal? RecommendedRelease = null,
    IReadOnlyList<ReportEmailItem>? Emails = null, bool CanEmail = false, string? EmailBlockedReason = null);

/// <summary>One email of the report to the bank (sent or failed).</summary>
public sealed record ReportEmailItem(Guid Id, DateTimeOffset At, string To, string? Cc, string Subject, int ReportNumber, string SentByName,
    EmailStatus Status, string? Error);

/// <summary>A ready-to-send email: the latest signed report, the branch's address and standard wording.</summary>
public sealed record ReportEmailDraft(Guid FileId, int ReportNumber, string FileName, long SizeBytes, IReadOnlyList<string> To,
    IReadOnlyList<string> Cc, string Subject, string Message, string? ReplyTo, string BankName, string? BranchName, bool SentBefore);

public sealed record SendReportEmailRequest(Guid FileId, IReadOnlyList<string> To, IReadOnlyList<string>? Cc, string Subject, string Message);

public sealed class SendReportEmailRequestValidator : AbstractValidator<SendReportEmailRequest>
{
    public SendReportEmailRequestValidator()
    {
        RuleFor(x => x.To).NotEmpty().WithMessage("Add at least one recipient.")
            .Must(x => x.Count <= 10).WithMessage("At most 10 recipients.");
        RuleForEach(x => x.To).NotEmpty().EmailAddress().WithMessage("'{PropertyValue}' is not a valid email address.").OverridePropertyName("to");
        RuleFor(x => x.Cc).Must(x => x is null || x.Count <= 10).WithMessage("At most 10 copies.");
        RuleForEach(x => x.Cc).NotEmpty().EmailAddress().WithMessage("'{PropertyValue}' is not a valid email address.").OverridePropertyName("cc");
        RuleFor(x => x.Subject).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Message).NotEmpty().MaximumLength(4000);
    }
}

public sealed record SaveReportRequest(DateOnly? ReportDate, DateOnly? ValuationDate, string? Opinion, string? Declaration, string? Place, int PhotosPerPage);

public sealed record ReportPdf(Stream Content, string FileName);

public interface IReportService
{
    Task<IReadOnlyList<ReportListItem>> ListAsync(ReportQuery query, CancellationToken ct = default);
    Task<Result<ReportDetail>> GetAsync(Guid assignmentId, CancellationToken ct = default);
    Task<Result<ReportDetail>> SaveAsync(Guid assignmentId, SaveReportRequest request, CancellationToken ct = default);
    /// <summary>Generates a draft PDF (watermarked, unsigned). The first draft moves the case to "Draft Prepared".</summary>
    Task<Result<ReportDetail>> GenerateDraftAsync(Guid assignmentId, CancellationToken ct = default);
    /// <summary>Reviewer approves: generates the final PDF signed by the case's valuer and moves the case to "Submitted".</summary>
    Task<Result<ReportDetail>> ApproveAsync(Guid assignmentId, CancellationToken ct = default);
    Task<Result<ReportPdf>> OpenAsync(Guid fileId, CancellationToken ct = default);
    /// <summary>The email to send: latest signed report, branch address, standard wording.</summary>
    Task<Result<ReportEmailDraft>> EmailDraftAsync(Guid assignmentId, CancellationToken ct = default);
    /// <summary>Emails a signed report to the bank and logs it on the case's timeline.</summary>
    Task<Result<ReportDetail>> EmailAsync(Guid assignmentId, SendReportEmailRequest request, CancellationToken ct = default);
}

// ---------------- what the template prints ----------------

public sealed record FirmBlock(string Name, string? Address, string? Phone, string? Email, string? RegisteredValuerNo, string? IbbiRegistration,
    byte[]? Logo, string Color);

public sealed record DocumentRow(string Name, DocumentAvailability Availability, string? Number, DateOnly? IssuedOn, string? IssuedBy, string? Notes);

public sealed record PhotoBlock(int Number, string Label, string? Caption, DateTimeOffset TakenAt, decimal? Latitude, decimal? Longitude, byte[] Image);

public sealed record SignerBlock(string Name, string? RegistrationNo, byte[]? Signature, string Method);

/// <summary>Everything a report template needs, already gathered and formatted where it matters.</summary>
public sealed record ReportModel(
    bool Draft,
    int Version,
    FirmBlock Firm,
    string Number,
    DateOnly ReportDate,
    DateOnly ValuationDate,
    DateOnly? InspectionDate,
    string BankName,
    string? BranchName,
    string? BranchAddress,
    string? BankReference,
    string? Purpose,
    string ApplicantName,
    string? OwnerName,
    PropertyType? PropertyType,
    string PropertyAddress,
    string? SurveyNo,
    string? CtsNo,
    string? PlotNo,
    decimal? Latitude,
    decimal? Longitude,
    InspectionData? Inspection,
    string? PersonMet,
    IReadOnlyList<DocumentRow> Documents,
    ValuationInput Valuation,
    ValuationResult Result,
    IReadOnlyList<PhotoBlock> Photos,
    int PhotosPerPage,
    string? Opinion,
    string Declaration,
    string? Place,
    SignerBlock? Signer,
    string? ApprovedBy,
    CaseType CaseType = CaseType.Valuation,
    string? PreviousNumber = null,
    decimal? PreviousPercent = null,
    DateOnly? PreviousDate = null);

/// <summary>Turns a report model into a PDF. One default template now; bank-specific templates later.</summary>
public interface IReportRenderer
{
    byte[] Render(ReportModel model);
}
