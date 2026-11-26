using ValuPro.Domain.Assignments;
using ValuPro.Domain.Clients;
using ValuPro.Domain.Properties;

namespace ValuPro.Application.Assignments;

public sealed record AssignmentListItem(
    Guid Id,
    string Number,
    AssignmentStatus Status,
    Priority Priority,
    Guid BankId,
    string BankShortCode,
    string BankName,
    string BankColor,
    Guid? BranchId,
    string? BranchName,
    string ApplicantName,
    Guid? ClientId,
    string PropertySummary,
    Guid? PropertyId,
    Guid ValuerId,
    string ValuerName,
    DateOnly DueDate,
    decimal Fee,
    bool IsOverdue,
    DateTimeOffset CreatedAt,
    CaseType CaseType = CaseType.Valuation);

public sealed record AssignmentEventItem(
    DateTimeOffset At,
    string ByName,
    AssignmentStatus? FromStatus,
    AssignmentStatus ToStatus,
    string Title,
    string? Note);

/// <summary>What the signed-in user may do with this assignment right now.</summary>
public sealed record AllowedMoves(AssignmentStatus? Next, bool CanMoveNext, string? NextBlockedReason, AssignmentStatus? Back, bool CanCancel, bool CanReopen, bool CanEdit, bool CanDelete);

public sealed record AssignmentDetail(
    AssignmentListItem Assignment,
    string? BankReference,
    string? Purpose,
    string? ApplicantPhone,
    Guid? ReviewerId,
    string? ReviewerName,
    DateTimeOffset? SiteVisitAt,
    string? Remarks,
    IReadOnlyList<AssignmentEventItem> Events,
    AllowedMoves Moves,
    Guid? PreviousAssignmentId = null,
    string? PreviousNumber = null);

public sealed record AssignmentQuery(
    string? Search = null,
    AssignmentStatus? Status = null,
    Guid? BankId = null,
    Priority? Priority = null,
    Guid? ValuerId = null,
    Guid? ClientId = null,
    Guid? PropertyId = null,
    bool Mine = false,
    bool Overdue = false,
    bool OpenOnly = false,
    string? Sort = null,
    bool Desc = false,
    int Page = 1,
    int PageSize = 20);

public sealed record PipelineStage(AssignmentStatus Status, int Count);

public sealed record AssignmentPipeline(IReadOnlyList<PipelineStage> Stages, int Open, int Overdue, int DueThisWeek, int Cancelled);

public sealed record SaveAssignmentRequest(
    Guid BankId,
    Guid? BranchId,
    string? BankReference,
    string? Purpose,
    string ApplicantName,
    Guid? ClientId,
    string? ApplicantPhone,
    string PropertySummary,
    Guid? PropertyId,
    Guid ValuerId,
    Guid? ReviewerId,
    Priority Priority,
    DateOnly DueDate,
    DateTimeOffset? SiteVisitAt,
    decimal Fee,
    string? Remarks,
    CaseType CaseType = CaseType.Valuation,
    Guid? PreviousAssignmentId = null);

public sealed record MoveRequest(AssignmentStatus To, string? Note, DateTimeOffset? SiteVisitAt);

// ---- lookups for the assignment form ----

public sealed record BranchOption(Guid Id, string Name, string? City);
public sealed record BankOption(Guid Id, string Name, string ShortCode, string BrandColor, IReadOnlyList<BranchOption> Branches);
public sealed record PersonOption(Guid Id, string Name, string Role);
public sealed record AssignmentLookups(IReadOnlyList<BankOption> Banks, IReadOnlyList<PersonOption> Valuers, IReadOnlyList<PersonOption> Reviewers, string NumberPreview);

public sealed record ClientOption(Guid Id, string Code, string Name, string Mobile, ClientType Type, string? City);
public sealed record PropertyOption(Guid Id, string Code, string Summary, string OwnerName, PropertyType Type);
