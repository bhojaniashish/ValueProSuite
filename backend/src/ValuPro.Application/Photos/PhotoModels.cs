using FluentValidation;
using ValuPro.Application.Common;
using ValuPro.Domain.Assignments;
using ValuPro.Domain.Inspections;

namespace ValuPro.Application.Photos;

public sealed record PhotoItem(
    Guid Id,
    Guid AssignmentId,
    Guid ClientKey,
    PhotoTag Tag,
    string? Caption,
    int? Width,
    int? Height,
    long SizeBytes,
    decimal? Latitude,
    decimal? Longitude,
    int? DistanceFromPropertyMetres,
    DateTimeOffset TakenAt,
    string TakenByName,
    PhotoSource Source,
    bool Stamped,
    bool IncludeInReport,
    int SortOrder);

/// <summary>"needs attention": no location on a site photo, or taken further from the property than allowed.</summary>
public sealed record PhotoQuery(
    string? Search = null,
    PhotoTag? Tag = null,
    Guid? AssignmentId = null,
    bool Mine = false,
    bool Attention = false,
    DateOnly? From = null,
    DateOnly? To = null,
    int Page = 1,
    int PageSize = 60);

public sealed record GalleryItem(PhotoItem Photo, string Number, string BankShortCode, string BankColor, string ApplicantName);

public sealed record GallerySummary(int Total, int Attention, IReadOnlyDictionary<PhotoTag, int> ByTag);

public sealed record GalleryPage(PagedResult<GalleryItem> Page, GallerySummary Summary);

/// <summary>All photos of one case with what the signed-in user may do.</summary>
public sealed record CasePhotos(
    Guid AssignmentId,
    string Number,
    AssignmentStatus Status,
    string BankShortCode,
    string BankColor,
    string ApplicantName,
    string PropertySummary,
    decimal? PropertyLatitude,
    decimal? PropertyLongitude,
    bool InspectionSubmitted,
    IReadOnlyList<PhotoItem> Photos,
    bool CanEdit,
    bool CanDelete,
    string? ReadOnlyReason);

public sealed record PhotoUpload(
    Guid ClientKey,
    PhotoTag Tag,
    string? Caption,
    decimal? Latitude,
    decimal? Longitude,
    DateTimeOffset? TakenAt,
    int? Width,
    int? Height,
    PhotoSource Source,
    bool Stamped);

public sealed record UpdatePhotoRequest(PhotoTag Tag, string? Caption, bool IncludeInReport);

/// <summary>The case's photos in their new order (every photo of the case, once).</summary>
public sealed record ReorderPhotosRequest(IReadOnlyList<Guid> PhotoIds);

public sealed record PhotoFile(Stream Content, string ContentType, string FileName);

public interface IPhotoService
{
    Task<GalleryPage> GalleryAsync(PhotoQuery query, CancellationToken ct = default);
    Task<Result<CasePhotos>> GetCaseAsync(Guid assignmentId, CancellationToken ct = default);
    /// <summary>The case's photos in report order (used by the inspection and the assignment page).</summary>
    Task<IReadOnlyList<PhotoItem>> ListAsync(Guid assignmentId, CancellationToken ct = default);

    Task<Result<PhotoItem>> UploadAsync(Guid assignmentId, PhotoUpload meta, Stream image, Stream thumb, string contentType, long size, CancellationToken ct = default);
    Task<Result<PhotoItem>> UpdateAsync(Guid photoId, UpdatePhotoRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<PhotoItem>>> ReorderAsync(Guid assignmentId, ReorderPhotosRequest request, CancellationToken ct = default);
    Task<Result<bool>> DeleteAsync(Guid photoId, CancellationToken ct = default);
    Task<Result<PhotoFile>> OpenAsync(Guid photoId, bool thumb, CancellationToken ct = default);
    /// <summary>All the case's photos as a ZIP, named in report order ("01 Front elevation.jpg").</summary>
    Task<Result<PhotoFile>> ZipAsync(Guid assignmentId, bool reportOnly, CancellationToken ct = default);
}

public sealed class PhotoQueryValidator : AbstractValidator<PhotoQuery>
{
    public PhotoQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).When(x => x.From is not null && x.To is not null).WithMessage("The end date is before the start date.");
    }
}

public sealed class UpdatePhotoRequestValidator : AbstractValidator<UpdatePhotoRequest>
{
    public UpdatePhotoRequestValidator()
    {
        RuleFor(x => x.Tag).IsInEnum();
        RuleFor(x => x.Caption).MaximumLength(200);
    }
}

public sealed class ReorderPhotosRequestValidator : AbstractValidator<ReorderPhotosRequest>
{
    public ReorderPhotosRequestValidator()
    {
        RuleFor(x => x.PhotoIds).NotEmpty().Must(ids => ids.Distinct().Count() == ids.Count).WithMessage("Each photo can appear only once.");
        RuleFor(x => x.PhotoIds.Count).LessThanOrEqualTo(500);
    }
}
