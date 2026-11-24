using Microsoft.AspNetCore.Mvc;
using ValuPro.Api.Security;
using ValuPro.Application.Inspections;
using ValuPro.Domain.Security;

namespace ValuPro.Api.Controllers;

/// <summary>Site inspections, keyed by assignment id (one inspection per assignment). Photos are in PhotosController.</summary>
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
}
