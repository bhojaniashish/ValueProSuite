using FluentValidation;
using ValuPro.Application.Common;
using ValuPro.Domain.Assignments;
using ValuPro.Domain.Banks;
using ValuPro.Domain.Properties;
using ValuPro.Domain.Rates;
using ValuPro.Domain.Valuations;

namespace ValuPro.Application.Valuations;

/// <summary>Cases waiting for a valuation (inspection done, not yet valued) or valued.</summary>
public sealed record ValuationQuery(string? Search = null, bool Done = false, bool Mine = true);

public sealed record ValuationListItem(
    Guid AssignmentId,
    string Number,
    AssignmentStatus Status,
    string BankShortCode,
    string BankColor,
    string ApplicantName,
    string PropertySummary,
    string ValuerName,
    DateOnly DueDate,
    decimal? FairMarketValue,
    DateTimeOffset? ValuedAt);

public sealed record ValuationCase(
    Guid AssignmentId,
    string Number,
    AssignmentStatus Status,
    string BankName,
    string BankShortCode,
    string BankColor,
    string ApplicantName,
    string PropertySummary,
    PropertyType? PropertyType,
    string? City,
    string ValuerName);

/// <summary>What the bank wants (from Banks).</summary>
public sealed record BankFactors(RateBasis RateBasis, DepreciationMethod Depreciation, decimal RealizablePercent, decimal DistressPercent);

/// <summary>What the site inspection found, for reference next to the inputs.</summary>
public sealed record InspectionFacts(
    bool Submitted,
    decimal? LandDocs, decimal? LandMeasured,
    decimal? CarpetDocs, decimal? CarpetMeasured,
    decimal? BuiltUpDocs, decimal? BuiltUpMeasured,
    decimal? SuperBuiltUpDocs,
    string? StructureType, int? AgeYears, string? Condition, string? Marketability);

/// <summary>Current Rate Master entries offered for this case. Circle rates are per sq.m; the rest per sq.ft.</summary>
public sealed record LocationOption(Guid Id, string Locality, string City, RateZone Zone, decimal MinRate, decimal MaxRate,
    decimal? CircleResidential, decimal? CircleCommercial, decimal? CircleOpenLand, CircleScheme? Scheme, DateOnly EffectiveFrom, bool Suggested);
public sealed record ConstructionOption(Guid Id, string ConstructionType, decimal? CpwdPerSqFt, decimal? PwdPerSqFt, decimal? BankPerSqFt, bool Suggested);
public sealed record DepreciationOption(string StructureType, int LifeYears, decimal SalvagePercent);

public sealed record ValuationOptions(
    IReadOnlyList<LocationOption> Locations,
    IReadOnlyList<ConstructionOption> Construction,
    IReadOnlyList<DepreciationOption> Depreciation);

public sealed record ValuationDetail(
    ValuationCase Case,
    bool Saved,
    int Version,
    DateTimeOffset? ValuedAt,
    string? ValuedByName,
    ValuationInput Input,
    ValuationResult Result,
    BankFactors Bank,
    InspectionFacts Inspection,
    ValuationOptions Options,
    /// <summary>Where the starting figures came from (shown until the valuation is first saved).</summary>
    IReadOnlyList<string> Suggestions,
    bool CanEdit,
    string? ReadOnlyReason);

public sealed record SaveValuationRequest(int Version, ValuationInput Input);

public interface IValuationService
{
    Task<IReadOnlyList<ValuationListItem>> ListAsync(ValuationQuery query, CancellationToken ct = default);
    Task<Result<ValuationDetail>> GetAsync(Guid assignmentId, CancellationToken ct = default);
    /// <summary>Works out the values without saving (the page calls it as the valuer types).</summary>
    ValuationResult Calculate(ValuationInput input);
    Task<Result<ValuationDetail>> SaveAsync(Guid assignmentId, SaveValuationRequest request, CancellationToken ct = default);
}

public sealed class ValuationInputValidator : AbstractValidator<ValuationInput>
{
    public ValuationInputValidator()
    {
        RuleFor(x => x.Method).IsInEnum();
        RuleFor(x => x.AreaBasis).IsInEnum();
        RuleFor(x => x.LandAreaSqFt).InclusiveBetween(0, 100_000_000).WithMessage("Land area looks wrong.");
        RuleFor(x => x.AreaSqFt).InclusiveBetween(0, 10_000_000).WithMessage("Area looks wrong.");
        RuleFor(x => x.LandRatePerSqFt).InclusiveBetween(0, 10_000_000);
        RuleFor(x => x.CompositeRatePerSqFt).InclusiveBetween(0, 10_000_000);
        RuleFor(x => x.CircleRatePerSqM).InclusiveBetween(0, 100_000_000);
        RuleFor(x => x.RealizablePercent).InclusiveBetween(1, 100).WithMessage("Realizable value should be 1–100% of market value.");
        RuleFor(x => x.DistressPercent).InclusiveBetween(1, 100).WithMessage("Distress value should be 1–100% of market value.");
        RuleFor(x => x).Must(x => x.DistressPercent <= x.RealizablePercent).WithName("distressPercent")
            .WithMessage("Distress value is usually not above the realizable value.");
        RuleFor(x => x.Building.Count).LessThanOrEqualTo(30);
        RuleForEach(x => x.Building).ChildRules(b =>
        {
            b.RuleFor(l => l.AreaSqFt).InclusiveBetween(0, 10_000_000);
            b.RuleFor(l => l.RatePerSqFt).InclusiveBetween(0, 1_000_000);
            b.RuleFor(l => l.AgeYears).InclusiveBetween(0, 300).WithMessage("Age should be 0–300 years.");
            b.RuleFor(l => l.LifeYears).InclusiveBetween(1, 300).WithMessage("Life should be 1–300 years.");
            b.RuleFor(l => l.SalvagePercent).InclusiveBetween(0, 50).WithMessage("Salvage should be 0–50%.");
        });
        RuleFor(x => x.Adjustments.Count).LessThanOrEqualTo(20);
        RuleForEach(x => x.Adjustments).ChildRules(a => a.RuleFor(l => l.Percent).InclusiveBetween(-50, 50).WithMessage("Adjustments should be within ±50%."));
        RuleFor(x => x.Extras.Count).LessThanOrEqualTo(20);
        RuleFor(x => x.Remarks).MaximumLength(2000);
    }
}

public sealed class SaveValuationRequestValidator : AbstractValidator<SaveValuationRequest>
{
    public SaveValuationRequestValidator()
    {
        RuleFor(x => x.Version).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Input).NotNull().SetValidator(new ValuationInputValidator());
    }
}
