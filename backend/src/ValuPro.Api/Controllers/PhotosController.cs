using Microsoft.AspNetCore.Mvc;
using ValuPro.Api.Security;
using ValuPro.Application.Common;
using ValuPro.Application.Photos;
using ValuPro.Domain.Inspections;
using ValuPro.Domain.Security;
using ValuPro.Infrastructure.Photos;

namespace ValuPro.Api.Controllers;

/// <summary>Case photos: the firm-wide gallery, one case's photos (order, report selection), uploads and downloads.</summary>
[Route("api/photos")]
[HasPermission(Permissions.Inspections.View)]
public sealed class PhotosController(IPhotoService photos) : ApiControllerBase
{
    /// <summary>All photos the firm has, newest first, with counts per type and how many need a second look.</summary>
    [HttpGet]
    [ProducesResponseType<GalleryPage>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Gallery([FromQuery] PhotoQuery query, CancellationToken ct) =>
        await ValidateAsync(query, ct) ?? Ok(await photos.GalleryAsync(query, ct));

    /// <summary>One case's photos in report order, with what the signed-in user may change.</summary>
    [HttpGet("cases/{assignmentId:guid}")]
    [ProducesResponseType<CasePhotos>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Case(Guid assignmentId, CancellationToken ct) => From(await photos.GetCaseAsync(assignmentId, ct));

    /// <summary>
    /// Adds a photo: the image (resized on the device) and a small thumbnail, with what it shows and where/when it was taken.
    /// Sending the same clientKey again returns the photo already saved.
    /// </summary>
    [HttpPost("cases/{assignmentId:guid}")]
    [HasPermission(Permissions.Inspections.Manage)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    [ProducesResponseType<PhotoItem>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Upload(Guid assignmentId, [FromForm] PhotoForm form, CancellationToken ct)
    {
        if (form.File is null) return Problem(Error.Validation("Choose a photo.", "file"));
        if (form.Thumb is null || form.Thumb.Length > PhotoService.MaxThumbBytes) return Problem(Error.Validation("The thumbnail is missing or too large.", "thumb"));
        await using var image = form.File.OpenReadStream();
        await using var thumb = form.Thumb.OpenReadStream();
        var meta = new PhotoUpload(form.ClientKey, form.Tag, form.Caption, form.Latitude, form.Longitude, form.TakenAt, form.Width, form.Height, form.Source, form.Stamped);
        return From(await photos.UploadAsync(assignmentId, meta, image, thumb, form.File.ContentType, form.File.Length, ct));
    }

    /// <summary>Saves the new report order of the case's photos.</summary>
    [HttpPut("cases/{assignmentId:guid}/order")]
    [HasPermission(Permissions.Inspections.Manage)]
    [ProducesResponseType<IReadOnlyList<PhotoItem>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reorder(Guid assignmentId, ReorderPhotosRequest request, CancellationToken ct) =>
        await ValidateAsync(request, ct) ?? From(await photos.ReorderAsync(assignmentId, request, ct));

    /// <summary>The case's photos as a ZIP in report order, with a photos.csv listing time and location. ?report=true: only those in the report.</summary>
    [HttpGet("cases/{assignmentId:guid}/zip")]
    public async Task<IActionResult> Zip(Guid assignmentId, [FromQuery] bool report, CancellationToken ct)
    {
        var result = await photos.ZipAsync(assignmentId, report, ct);
        return result.IsSuccess ? File(result.Value!.Content, result.Value.ContentType, result.Value.FileName) : Problem(result.Error!);
    }

    [HttpPut("{photoId:guid}")]
    [HasPermission(Permissions.Inspections.Manage)]
    [ProducesResponseType<PhotoItem>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid photoId, UpdatePhotoRequest request, CancellationToken ct) =>
        await ValidateAsync(request, ct) ?? From(await photos.UpdateAsync(photoId, request, ct));

    [HttpDelete("{photoId:guid}")]
    [HasPermission(Permissions.Inspections.Manage)]
    public async Task<IActionResult> Delete(Guid photoId, CancellationToken ct)
    {
        var result = await photos.DeleteAsync(photoId, ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error!);
    }

    /// <summary>The photo file (or its thumbnail with ?thumb=true). Cached privately by the browser.</summary>
    [HttpGet("{photoId:guid}/file")]
    public async Task<IActionResult> PhotoFile(Guid photoId, [FromQuery] bool thumb, [FromQuery] bool download, CancellationToken ct)
    {
        var result = await photos.OpenAsync(photoId, thumb, ct);
        if (!result.IsSuccess) return Problem(result.Error!);
        Response.Headers.CacheControl = "private, max-age=86400";
        return download
            ? File(result.Value!.Content, result.Value.ContentType, result.Value.FileName)
            : File(result.Value!.Content, result.Value.ContentType);
    }

    public sealed class PhotoForm
    {
        public IFormFile? File { get; set; }
        public IFormFile? Thumb { get; set; }
        public Guid ClientKey { get; set; }
        public PhotoTag Tag { get; set; }
        public string? Caption { get; set; }
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        public DateTimeOffset? TakenAt { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
        public PhotoSource Source { get; set; } = PhotoSource.Upload;
        public bool Stamped { get; set; }
    }
}
