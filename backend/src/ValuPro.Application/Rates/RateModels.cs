using FluentValidation;
using ValuPro.Application.Common;
using ValuPro.Domain.Banks;
using ValuPro.Domain.Rates;

namespace ValuPro.Application.Rates;

public enum RateKind
{
    Location,
    Circle,
    Construction,
}

public sealed record RateQuery(
    string? Search = null,
    string? City = null,
    string? State = null,
    RateZone? Zone = null,
    CircleScheme? Scheme = null,
    bool CurrentOnly = true,
    int Page = 1,
    int PageSize = 25);

/// <summary>A location's market rate, with the matching current circle rate (per sq.m) when one is on file.</summary>
public sealed record LocationRateItem(
    Guid Id, string Locality, string City, string? State, RateZone Zone, decimal MinRate, decimal MaxRate,
    DateOnly EffectiveFrom, string? Source, bool IsCurrent, decimal? CircleRatePerSqM, CircleScheme? CircleScheme);

public sealed record CircleRateItem(
    Guid Id, string State, CircleScheme Scheme, string City, string Locality, decimal? Residential, decimal? Commercial,
    decimal? OpenLand, DateOnly EffectiveFrom, string? Reference, bool IsCurrent);

public sealed record ConstructionRateItem(
    Guid Id, string ConstructionType, RateUnit Unit, decimal? CpwdRate, decimal? PwdRate, decimal? BankRate,
    DateOnly EffectiveFrom, string? Remarks, bool IsCurrent);

public sealed record DepreciationBand(string Label, decimal FromYears, decimal ToYears, decimal Percent);

public sealed record DepreciationRuleItem(
    Guid Id, string StructureType, int TotalLifeYears, decimal SalvagePercent, DepreciationMethod Method, string? Remarks,
    IReadOnlyList<DepreciationBand> Bands);

public sealed record SaveLocationRate(string Locality, string City, string? State, RateZone Zone, decimal MinRate, decimal MaxRate, DateOnly EffectiveFrom, string? Source);
public sealed record SaveCircleRate(string State, CircleScheme Scheme, string City, string Locality, decimal? Residential, decimal? Commercial, decimal? OpenLand, DateOnly EffectiveFrom, string? Reference);
public sealed record SaveConstructionRate(string ConstructionType, RateUnit Unit, decimal? CpwdRate, decimal? PwdRate, decimal? BankRate, DateOnly EffectiveFrom, string? Remarks);
public sealed record SaveDepreciationRule(string StructureType, int TotalLifeYears, decimal SalvagePercent, DepreciationMethod Method, string? Remarks);

public sealed record RateFilters(IReadOnlyList<string> Cities, IReadOnlyList<string> States, int Location, int Circle, int Construction, int Depreciation);

/// <summary>One checked row of an uploaded rate file. <c>Data</c> is the row as it would be saved.</summary>
public sealed record RateImportRow(int Row, object? Data, IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0 && Data is not null;
}

public sealed record RateImportPreview(RateKind Kind, IReadOnlyList<RateImportRow> Rows, int Valid, int Invalid);
public sealed record RateImportResult(int Imported, int Rejected);

public interface IRateService
{
    Task<RateFilters> GetFiltersAsync(CancellationToken ct = default);

    Task<PagedResult<LocationRateItem>> ListLocationAsync(RateQuery q, CancellationToken ct = default);
    Task<Result<LocationRateItem>> SaveLocationAsync(Guid? id, SaveLocationRate r, CancellationToken ct = default);

    Task<PagedResult<CircleRateItem>> ListCircleAsync(RateQuery q, CancellationToken ct = default);
    Task<Result<CircleRateItem>> SaveCircleAsync(Guid? id, SaveCircleRate r, CancellationToken ct = default);

    Task<PagedResult<ConstructionRateItem>> ListConstructionAsync(RateQuery q, CancellationToken ct = default);
    Task<Result<ConstructionRateItem>> SaveConstructionAsync(Guid? id, SaveConstructionRate r, CancellationToken ct = default);

    Task<IReadOnlyList<DepreciationRuleItem>> ListDepreciationAsync(CancellationToken ct = default);
    Task<Result<DepreciationRuleItem>> SaveDepreciationAsync(Guid? id, SaveDepreciationRule r, CancellationToken ct = default);
    /// <summary>Adds the common structure types (RCC framed, load bearing, steel shed, temporary) that are not there yet.</summary>
    Task<IReadOnlyList<DepreciationRuleItem>> AddStandardDepreciationAsync(CancellationToken ct = default);

    Task<Result<bool>> DeleteAsync(string kind, Guid id, CancellationToken ct = default);

    string Template(RateKind kind);
    Task<Result<RateImportPreview>> PreviewImportAsync(RateKind kind, Stream file, string fileName, CancellationToken ct = default);
    Task<Result<RateImportResult>> ImportAsync(RateKind kind, Stream file, string fileName, CancellationToken ct = default);
}

// ---------------- validation ----------------

public sealed class SaveLocationRateValidator : AbstractValidator<SaveLocationRate>
{
    public SaveLocationRateValidator()
    {
        RuleFor(x => x.Locality).NotEmpty().WithMessage("Enter the locality.").MaximumLength(120);
        RuleFor(x => x.City).NotEmpty().WithMessage("Enter the city.").MaximumLength(100);
        RuleFor(x => x.State).MaximumLength(60);
        RuleFor(x => x.Zone).IsInEnum();
        RuleFor(x => x.MinRate).GreaterThan(0).WithMessage("Enter the lowest market rate per sq.ft.").LessThan(10_000_000);
        RuleFor(x => x.MaxRate).GreaterThanOrEqualTo(x => x.MinRate).WithMessage("The highest rate cannot be below the lowest.").LessThan(10_000_000);
        RuleFor(x => x.EffectiveFrom).NotEmpty().WithMessage("Enter the date these rates apply from.");
        RuleFor(x => x.Source).MaximumLength(200);
    }
}

public sealed class SaveCircleRateValidator : AbstractValidator<SaveCircleRate>
{
    public SaveCircleRateValidator()
    {
        RuleFor(x => x.State).NotEmpty().WithMessage("Choose the state.").MaximumLength(60);
        RuleFor(x => x.Scheme).IsInEnum();
        RuleFor(x => x.City).NotEmpty().WithMessage("Enter the city or district.").MaximumLength(100);
        RuleFor(x => x.Locality).NotEmpty().WithMessage("Enter the locality / village.").MaximumLength(120);
        RuleFor(x => x).Must(x => x.Residential is not null || x.Commercial is not null || x.OpenLand is not null)
            .WithName("residential").WithMessage("Enter at least one rate.");
        RuleFor(x => x.Residential).GreaterThan(0).LessThan(100_000_000).When(x => x.Residential is not null);
        RuleFor(x => x.Commercial).GreaterThan(0).LessThan(100_000_000).When(x => x.Commercial is not null);
        RuleFor(x => x.OpenLand).GreaterThan(0).LessThan(100_000_000).When(x => x.OpenLand is not null);
        RuleFor(x => x.EffectiveFrom).NotEmpty().WithMessage("Enter the date these rates apply from.");
        RuleFor(x => x.Reference).MaximumLength(200);
    }
}

public sealed class SaveConstructionRateValidator : AbstractValidator<SaveConstructionRate>
{
    public SaveConstructionRateValidator()
    {
        RuleFor(x => x.ConstructionType).NotEmpty().WithMessage("Enter the construction type.").MaximumLength(120);
        RuleFor(x => x.Unit).IsInEnum();
        RuleFor(x => x).Must(x => x.CpwdRate is not null || x.PwdRate is not null || x.BankRate is not null)
            .WithName("cpwdRate").WithMessage("Enter at least one rate.");
        RuleFor(x => x.CpwdRate).GreaterThan(0).LessThan(10_000_000).When(x => x.CpwdRate is not null);
        RuleFor(x => x.PwdRate).GreaterThan(0).LessThan(10_000_000).When(x => x.PwdRate is not null);
        RuleFor(x => x.BankRate).GreaterThan(0).LessThan(10_000_000).When(x => x.BankRate is not null);
        RuleFor(x => x.EffectiveFrom).NotEmpty();
        RuleFor(x => x.Remarks).MaximumLength(300);
    }
}

public sealed class SaveDepreciationRuleValidator : AbstractValidator<SaveDepreciationRule>
{
    public SaveDepreciationRuleValidator()
    {
        RuleFor(x => x.StructureType).NotEmpty().WithMessage("Enter the structure type.").MaximumLength(120);
        RuleFor(x => x.TotalLifeYears).InclusiveBetween(5, 150).WithMessage("Total life should be 5–150 years.");
        RuleFor(x => x.SalvagePercent).InclusiveBetween(0, 50).WithMessage("Salvage value should be 0–50%.");
        RuleFor(x => x.SalvagePercent).GreaterThan(0).WithMessage("Written-down value needs a salvage value above 0%.")
            .When(x => x.Method == DepreciationMethod.WrittenDownValue);
        RuleFor(x => x.Method).IsInEnum();
        RuleFor(x => x.Remarks).MaximumLength(300);
    }
}

public sealed class RateQueryValidator : AbstractValidator<RateQuery>
{
    public RateQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 500);
        RuleFor(x => x.Search).MaximumLength(100);
    }
}
