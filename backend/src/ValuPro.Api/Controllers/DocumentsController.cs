using Microsoft.AspNetCore.Mvc;
using ValuPro.Api.Security;
using ValuPro.Application.Common;
using ValuPro.Application.Documents;
using ValuPro.Domain.Security;

namespace ValuPro.Api.Controllers;

/// <summary>
/// Documents perused for each case (number, date, issuing office, original seen or not) with optional file versions.
/// Who may change a case's documents (office, the case's valuer or reviewer) is checked per case.
/// </summary>
[Route("api/documents")]
[HasPermission(Permissions.Assignments.View)]
public sealed class DocumentsController(IDocumentService documents) : ApiControllerBase
{
    /// <summary>All documents, newest first, with counts (with file, not provided, expiring, expired). Expiry=Expiring|Expired.</summary>
    [HttpGet]
    [ProducesResponseType<DocumentPage>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] DocumentQuery query, CancellationToken ct) =>
        await ValidateAsync(query, ct) ?? Ok(await documents.ListAsync(query, ct));

    /// <summary>One case's documents, the checklist for its property type and documents from earlier cases on the same property.</summary>
    [HttpGet("cases/{assignmentId:guid}")]
    [ProducesResponseType<CaseDocuments>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Case(Guid assignmentId, CancellationToken ct) => From(await documents.GetCaseAsync(assignmentId, ct));

    [HttpPost("cases/{assignmentId:guid}")]
    [ProducesResponseType<DocumentItem>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create(Guid assignmentId, SaveDocumentRequest request, CancellationToken ct) =>
        await ValidateAsync(request, ct) ?? From(await documents.CreateAsync(assignmentId, request, ct));

    /// <summary>Adds a document recorded on an earlier case of the same property (its files are reused, not copied).</summary>
    [HttpPost("cases/{assignmentId:guid}/reuse/{documentId:guid}")]
    [ProducesResponseType<DocumentItem>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reuse(Guid assignmentId, Guid documentId, CancellationToken ct) =>
        From(await documents.ReuseAsync(assignmentId, documentId, ct));

    /// <summary>The case's current document files as a ZIP, numbered by type.</summary>
    [HttpGet("cases/{assignmentId:guid}/zip")]
    public async Task<IActionResult> Zip(Guid assignmentId, CancellationToken ct)
    {
        var r = await documents.ZipAsync(assignmentId, ct);
        return r.IsSuccess ? File(r.Value!.Content, r.Value.ContentType, r.Value.FileName) : Problem(r.Error!);
    }

    [HttpPut("{documentId:guid}")]
    [ProducesResponseType<DocumentItem>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid documentId, SaveDocumentRequest request, CancellationToken ct) =>
        await ValidateAsync(request, ct) ?? From(await documents.UpdateAsync(documentId, request, ct));

    [HttpDelete("{documentId:guid}")]
    public async Task<IActionResult> Delete(Guid documentId, CancellationToken ct)
    {
        var r = await documents.DeleteAsync(documentId, ct);
        return r.IsSuccess ? NoContent() : Problem(r.Error!);
    }

    /// <summary>Uploads a PDF or photo as the document's newest version (up to 20 MB); older versions are kept.</summary>
    [HttpPost("{documentId:guid}/files")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(21 * 1024 * 1024)]
    [ProducesResponseType<DocumentItem>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Upload(Guid documentId, IFormFile? file, CancellationToken ct)
    {
        if (file is null) return Problem(Error.Validation("Choose a file.", "file"));
        await using var stream = file.OpenReadStream();
        return From(await documents.UploadAsync(documentId, stream, file.FileName, file.ContentType, file.Length, ct));
    }

    /// <summary>A file version; shown in the browser (PDF viewer) unless ?download=true.</summary>
    [HttpGet("files/{fileId:guid}")]
    public async Task<IActionResult> OpenFile(Guid fileId, [FromQuery] bool download, CancellationToken ct)
    {
        var r = await documents.OpenFileAsync(fileId, ct);
        if (!r.IsSuccess) return Problem(r.Error!);
        Response.Headers.CacheControl = "private, max-age=3600";
        return download ? File(r.Value!.Content, r.Value.ContentType, r.Value.FileName) : File(r.Value!.Content, r.Value.ContentType);
    }
}
