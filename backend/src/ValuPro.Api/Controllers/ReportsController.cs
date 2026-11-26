using Microsoft.AspNetCore.Mvc;
using ValuPro.Api.Security;
using ValuPro.Application.Reports;
using ValuPro.Domain.Security;

namespace ValuPro.Api.Controllers;

/// <summary>Valuation reports per assignment: details, drafts, approval with signature, and the PDFs.</summary>
[Route("api/reports")]
[HasPermission(Permissions.Reports.View)]
public sealed class ReportsController(IReportService reports) : ApiControllerBase
{
    /// <summary>Cases by report stage: draft (to prepare), review (Quality Review), done (submitted).</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ReportListItem>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] ReportQuery query, CancellationToken ct) => Ok(await reports.ListAsync(query, ct));

    /// <summary>Readiness checks, the editable report details, the generated versions and what the user may do.</summary>
    [HttpGet("{assignmentId:guid}")]
    [ProducesResponseType<ReportDetail>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid assignmentId, CancellationToken ct) => From(await reports.GetAsync(assignmentId, ct));

    [HttpPut("{assignmentId:guid}")]
    [HasPermission(Permissions.Reports.Manage)]
    [ProducesResponseType<ReportDetail>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Save(Guid assignmentId, SaveReportRequest request, CancellationToken ct) =>
        From(await reports.SaveAsync(assignmentId, request, ct));

    /// <summary>Generates a draft PDF (watermarked, unsigned). The first one moves the case to "Draft Prepared".</summary>
    [HttpPost("{assignmentId:guid}/draft")]
    [HasPermission(Permissions.Reports.Manage)]
    [ProducesResponseType<ReportDetail>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Draft(Guid assignmentId, CancellationToken ct) => From(await reports.GenerateDraftAsync(assignmentId, ct));

    /// <summary>Approves the report: final PDF signed by the case's valuer; the case moves to "Submitted".</summary>
    [HttpPost("{assignmentId:guid}/approve")]
    [HasPermission(Permissions.Reports.Approve)]
    [ProducesResponseType<ReportDetail>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Approve(Guid assignmentId, CancellationToken ct) => From(await reports.ApproveAsync(assignmentId, ct));

    /// <summary>A generated PDF; shown in the browser unless ?download=true.</summary>
    [HttpGet("files/{fileId:guid}")]
    public async Task<IActionResult> Pdf(Guid fileId, [FromQuery] bool download, CancellationToken ct)
    {
        var r = await reports.OpenAsync(fileId, ct);
        if (!r.IsSuccess) return Problem(r.Error!);
        Response.Headers.CacheControl = "private, max-age=3600";
        return download ? File(r.Value!.Content, "application/pdf", r.Value.FileName) : File(r.Value!.Content, "application/pdf");
    }
}
