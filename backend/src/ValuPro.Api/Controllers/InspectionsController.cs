using Microsoft.AspNetCore.Mvc;
using ValuPro.Api.Security;
using ValuPro.Application.Common;
using ValuPro.Application.Inspections;
using ValuPro.Domain.Inspections;
using ValuPro.Domain.Security;
using ValuPro.Infrastructure.Inspections;

namespace ValuPro.Api.Controllers;

/// <summary>Site inspections, keyed by assignment id (one inspection per assignment), and the case's photos.</summary>
[Route("api/inspections")]
[HasPermission(Permissions.Inspections.View)]
public sealed class InspectionsController(IInspectionService inspections) : ApiControllerBase
{
    /// <summary>Site visits to do (new or scheduled cases) or done (submitted inspections). Mine = only my cases.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<InspectionListItem>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] InspectionQuery query, CancellationToken ct) => Ok(await inspections.ListAsync(query, ct));

    /// <summary>The case, what has been recorded so far (or a pre-filled blank), photos, the bank's questions and what is still missing.</summary>
    [HttpGet("{assignmentId:guid}")]
    [ProducesResponseType<InspectionDetail>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid assignmentId, CancellationToken ct) => From(await inspections.GetAsync(assignmentId, ct));

    /// <summary>Saves the whole inspection (called automatically as the valuer types). 409 when another device saved first.</summary>
    [HttpPut("{assignmentId:guid}")]
    [HasPermission(Permissions.Inspections.Manage)]
    [RequestSizeLimit(512 * 1024)]
    [ProducesResponseType<SaveInspectionResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Save(Guid assignmentId, SaveInspectionRequest request, CancellationToken ct) =>
        await ValidateAsync(request, ct) ?? From(await inspections.SaveAsync(assignmentId, request, ct));

    /// <summary>Checks everything needed is there, locks the inspection and moves the case to "Inspection Done".</summary>
    [HttpPost("{assignmentId:guid}/submit")]
    [HasPermission(Permissions.Inspections.Manage)]
    [ProducesResponseType<InspectionDetail>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Submit(Guid assignmentId, SubmitInspectionRequest request, CancellationToken ct) =>
        From(await inspections.SubmitAsync(assignmentId, request, ct));

    /// <summary>
    /// Adds a photo: the image (resized on the phone) and a small thumbnail, with what it shows and where/when it was taken.
    /// Sending the same clientKey again returns the photo already saved.
    /// </summary>
    [HttpPost("{assignmentId:guid}/photos")]
    [HasPermission(Permissions.Inspections.Manage)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    [ProducesResponseType<PhotoItem>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Upload(Guid assignmentId, [FromForm] PhotoForm form, CancellationToken ct)
    {
        if (form.File is null) return Problem(Error.Validation("Choose a photo.", "file"));
        if (form.Thumb is null || form.Thumb.Length > InspectionService.MaxThumbBytes) return Problem(Error.Validation("The thumbnail is missing or too large.", "thumb"));
        await using var image = form.File.OpenReadStream();
        await using var thumb = form.Thumb.OpenReadStream();
        var meta = new PhotoUpload(form.ClientKey, form.Tag, form.Caption, form.Latitude, form.Longitude, form.TakenAt, form.Width, form.Height);
        return From(await inspections.UploadPhotoAsync(assignmentId, meta, image, thumb, form.File.ContentType, form.File.Length, ct));
    }

    [HttpPut("photos/{photoId:guid}")]
    [HasPermission(Permissions.Inspections.Manage)]
    [ProducesResponseType<PhotoItem>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdatePhoto(Guid photoId, UpdatePhotoRequest request, CancellationToken ct) =>
        await ValidateAsync(request, ct) ?? From(await inspections.UpdatePhotoAsync(photoId, request, ct));

    [HttpDelete("photos/{photoId:guid}")]
    [HasPermission(Permissions.Inspections.Manage)]
    public async Task<IActionResult> DeletePhoto(Guid photoId, CancellationToken ct)
    {
        var result = await inspections.DeletePhotoAsync(photoId, ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error!);
    }

    /// <summary>The photo file (or its thumbnail with ?thumb=true). Cached privately by the browser.</summary>
    [HttpGet("photos/{photoId:guid}/file")]
    public async Task<IActionResult> PhotoFile(Guid photoId, [FromQuery] bool thumb, CancellationToken ct)
    {
        var result = await inspections.OpenPhotoAsync(photoId, thumb, ct);
        if (!result.IsSuccess) return Problem(result.Error!);
        Response.Headers.CacheControl = "private, max-age=86400";
        return File(result.Value!.Content, result.Value.ContentType);
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
    }
}
