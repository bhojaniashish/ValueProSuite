using FluentValidation;
using ValuPro.Application.Common;

namespace ValuPro.Application.Properties;

public interface IPropertyService
{
    Task<PagedResult<PropertyItem>> ListAsync(PropertyQuery query, CancellationToken ct = default);
    Task<PropertyFilters> GetFiltersAsync(CancellationToken ct = default);
    Task<Result<PropertyDetail>> GetAsync(Guid id, CancellationToken ct = default);
    Task<Result<PropertyItem>> CreateAsync(SavePropertyRequest request, CancellationToken ct = default);
    Task<Result<PropertyItem>> UpdateAsync(Guid id, SavePropertyRequest request, CancellationToken ct = default);

    /// <summary>Refused while the property has open assignments.</summary>
    Task<Result<bool>> DeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed class SavePropertyRequestValidator : AbstractValidator<SavePropertyRequest>
{
    public SavePropertyRequestValidator()
    {
        RuleFor(x => x.Type).IsInEnum().WithMessage("Choose a property type.");
        RuleFor(x => x.OwnerName).NotEmpty().WithMessage("Enter the owner's name.").MaximumLength(200);
        RuleFor(x => x.Address).NotEmpty().WithMessage("Enter the property address.").MaximumLength(500);
        RuleFor(x => x.City).MaximumLength(100);
        RuleFor(x => x.State).MaximumLength(60);
        RuleFor(x => x.Pincode).Matches("^[1-9][0-9]{5}$").WithMessage("Enter a 6-digit pincode.").When(x => !string.IsNullOrWhiteSpace(x.Pincode));
        RuleFor(x => x.SurveyNo).MaximumLength(60);
        RuleFor(x => x.CtsNo).MaximumLength(60);
        RuleFor(x => x.PlotNo).MaximumLength(60);
        RuleFor(x => x.LandArea).GreaterThan(0).LessThan(100_000_000).WithMessage("Enter the land area in sq.ft.").When(x => x.LandArea is not null);
        RuleFor(x => x.BuiltUpArea).GreaterThan(0).LessThan(100_000_000).WithMessage("Enter the built-up area in sq.ft.").When(x => x.BuiltUpArea is not null);
        RuleFor(x => x.Zone).MaximumLength(60);
        RuleFor(x => x.Usage).IsInEnum().When(x => x.Usage is not null);
        // India lies roughly within 6–38°N, 68–98°E; anything outside is almost certainly a typo or swapped fields.
        RuleFor(x => x.Latitude).InclusiveBetween(6m, 38m).WithMessage("Latitude in India is between 6 and 38.").When(x => x.Latitude is not null);
        RuleFor(x => x.Longitude).InclusiveBetween(68m, 98m).WithMessage("Longitude in India is between 68 and 98.").When(x => x.Longitude is not null);
        RuleFor(x => x).Must(x => (x.Latitude is null) == (x.Longitude is null))
            .WithName("latitude").WithMessage("Enter both latitude and longitude, or neither.");
        RuleFor(x => x.Remarks).MaximumLength(1000);
    }
}

public sealed class PropertyQueryValidator : AbstractValidator<PropertyQuery>
{
    public PropertyQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Search).MaximumLength(100);
    }
}
