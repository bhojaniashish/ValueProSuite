using FluentValidation;
using ValuPro.Application.Common;
using ValuPro.Application.Photos;
using ValuPro.Domain.Assignments;
using ValuPro.Domain.Banks;
using ValuPro.Domain.Inspections;
using ValuPro.Domain.Properties;

namespace ValuPro.Application.Inspections;

/// <summary>Site visits to do (case is new or visit scheduled) or done (inspection submitted).</summary>
public sealed record InspectionQuery(string? Search = null, bool Done = false, bool Mine = true);

public sealed record InspectionListItem(
    Guid AssignmentId,
    string Number,
    AssignmentStatus AssignmentStatus,
    string BankShortCode,
    string BankColor,
    string ApplicantName,
    string PropertySummary,
    DateTimeOffset? SiteVisitAt,
    DateOnly DueDate,
    string ValuerName,
    InspectionStatus? Status,
    DateTimeOffset? UpdatedAt,
    int PhotoCount);

/// <summary>The case being inspected: what the valuer needs to see at the gate.</summary>
public sealed record InspectionCase(
    Guid AssignmentId,
    string Number,
    AssignmentStatus Status,
    string BankName,
    string BankShortCode,
    string BankColor,
    string? BranchName,
    string ApplicantName,
    string? ApplicantPhone,
    string PropertySummary,
    Guid? PropertyId,
    PropertyType? PropertyType,
    string? PropertyAddress,
    decimal? PropertyLatitude,
    decimal? PropertyLongitude,
    decimal? PropertyLandArea,
    decimal? PropertyBuiltUpArea,
    DateTimeOffset? SiteVisitAt,
    DateOnly DueDate,
    string ValuerName);

public sealed record BankQuestionItem(string Text, AnswerType AnswerType, IReadOnlyList<string> Options, bool Required);

public sealed record InspectionDetail(
    InspectionCase Case,
    bool Started,
    InspectionStatus Status,
    DateTimeOffset? StartedAt,
    string? StartedByName,
    decimal? Latitude,
    decimal? Longitude,
    int? AccuracyMetres,
    DateTimeOffset? LocatedAt,
    int? DistanceFromPropertyMetres,
    bool FarFromProperty,
    DateTimeOffset? SubmittedAt,
    string? SubmittedByName,
    int Version,
    InspectionData Data,
    IReadOnlyList<PhotoItem> Photos,
    IReadOnlyList<BankQuestionItem> BankQuestions,
    IReadOnlyList<string> StructureTypes,
    bool LandOnly,
    IReadOnlyList<MissingItem> Missing,
    bool CanEdit,
    string? ReadOnlyReason,
    CaseType CaseType = CaseType.Valuation,
    PreviousProgress? Previous = null);

/// <summary>The earlier visit a stage inspection / re-inspection follows: its case number, overall completion and date.</summary>
public sealed record PreviousProgress(Guid AssignmentId, string Number, decimal? CompletionPercent, DateOnly? InspectedOn);

public sealed record GpsFix(decimal Latitude, decimal Longitude, int? AccuracyMetres, DateTimeOffset? At);

/// <summary>Saves the whole inspection. <see cref="Version"/> is 0 for the first save, then the number from the last response.</summary>
public sealed record SaveInspectionRequest(int Version, InspectionData Data, GpsFix? Location);

/// <summary>What a save returns: the new version and what is still missing (the phone keeps its own copy of the data).</summary>
public sealed record SaveInspectionResult(int Version, IReadOnlyList<MissingItem> Missing, int? DistanceFromPropertyMetres, bool FarFromProperty, DateTimeOffset SavedAt);

public sealed record SubmitInspectionRequest(int Version);

public interface IInspectionService
{
    Task<IReadOnlyList<InspectionListItem>> ListAsync(InspectionQuery query, CancellationToken ct = default);
    Task<Result<InspectionDetail>> GetAsync(Guid assignmentId, CancellationToken ct = default);
    Task<Result<SaveInspectionResult>> SaveAsync(Guid assignmentId, SaveInspectionRequest request, CancellationToken ct = default);
    Task<Result<InspectionDetail>> SubmitAsync(Guid assignmentId, SubmitInspectionRequest request, CancellationToken ct = default);
}

public sealed class SaveInspectionRequestValidator : AbstractValidator<SaveInspectionRequest>
{
    public SaveInspectionRequestValidator()
    {
        RuleFor(x => x.Version).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Data).NotNull();
        RuleFor(x => x.Location!.Latitude).InclusiveBetween(-90, 90).When(x => x.Location is not null).WithMessage("Latitude must be between -90 and 90.");
        RuleFor(x => x.Location!.Longitude).InclusiveBetween(-180, 180).When(x => x.Location is not null).WithMessage("Longitude must be between -180 and 180.");
        RuleFor(x => x.Data.Location.RoadWidthFt).InclusiveBetween(0, 1000).When(x => x.Data?.Location.RoadWidthFt is not null)
            .WithMessage("Road width looks wrong (0–1000 ft).");
        RuleFor(x => x.Data.Building.AgeYears).InclusiveBetween(0, 300).When(x => x.Data?.Building.AgeYears is not null)
            .WithMessage("Age should be 0–300 years.");
        RuleFor(x => x.Data.Building.CompletionPercent).InclusiveBetween(0, 100).When(x => x.Data?.Building.CompletionPercent is not null)
            .WithMessage("Completion should be 0–100%.");
        RuleFor(x => x.Data.Building.TotalFloors).InclusiveBetween(0, 200).When(x => x.Data?.Building.TotalFloors is not null)
            .WithMessage("Floors should be 0–200.");
        RuleFor(x => x.Data.Measurements.Floors.Count).LessThanOrEqualTo(60).When(x => x.Data is not null).WithMessage("Up to 60 floor rows.");
        RuleFor(x => x.Data.BankAnswers.Count).LessThanOrEqualTo(100).When(x => x.Data is not null);
        RuleFor(x => x.Data.Construction.Stages.Count).LessThanOrEqualTo(30).When(x => x.Data is not null).WithMessage("Up to 30 stages.");
        RuleForEach(x => x.Data.Construction.Stages).ChildRules(st =>
        {
            st.RuleFor(c => c.Weight).InclusiveBetween(0, 100).WithMessage("Stage weight should be 0–100%.");
            st.RuleFor(c => c.DonePercent).InclusiveBetween(0, 100).WithMessage("Stage completion should be 0–100%.");
        }).When(x => x.Data is not null);
        RuleFor(x => x.Data.Remarks.Text).MaximumLength(4000).When(x => x.Data is not null).WithMessage("Keep remarks under 4,000 characters.");
    }
}
