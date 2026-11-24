using Microsoft.AspNetCore.Mvc;
using ValuPro.Api.Security;
using ValuPro.Application.Assignments;
using ValuPro.Application.Common;
using ValuPro.Domain.Security;

namespace ValuPro.Api.Controllers;

[Route("api/assignments")]
[HasPermission(Permissions.Assignments.View)]
public sealed class AssignmentsController(IAssignmentService assignments) : ApiControllerBase
{
    /// <summary>Assignments with search, filters (status, bank, priority, valuer, client, property, mine, overdue, open), sorting and paging.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<AssignmentListItem>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] AssignmentQuery query, CancellationToken ct) =>
        await ValidateAsync(query, ct) ?? Ok(await assignments.ListAsync(query, ct));

    /// <summary>Counts per stage, open, overdue and due this week.</summary>
    [HttpGet("pipeline")]
    [ProducesResponseType<AssignmentPipeline>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Pipeline([FromQuery] bool mine, CancellationToken ct) => Ok(await assignments.GetPipelineAsync(mine, ct));

    /// <summary>Banks with branches, valuers, reviewers and the next number: everything the assignment form needs.</summary>
    [HttpGet("lookups")]
    [ProducesResponseType<AssignmentLookups>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Lookups(CancellationToken ct) => Ok(await assignments.GetLookupsAsync(ct));

    [HttpGet("lookups/clients")]
    [ProducesResponseType<IReadOnlyList<ClientOption>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Clients([FromQuery] string? search, CancellationToken ct) => Ok(await assignments.SearchClientsAsync(search, ct));

    [HttpGet("lookups/properties")]
    [ProducesResponseType<IReadOnlyList<PropertyOption>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Properties([FromQuery] string? search, CancellationToken ct) => Ok(await assignments.SearchPropertiesAsync(search, ct));

    /// <summary>The assignment with its timeline and what the signed-in user may do next.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<AssignmentDetail>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => From(await assignments.GetAsync(id, ct));

    [HttpPost]
    [HasPermission(Permissions.Assignments.Manage)]
    [ProducesResponseType<AssignmentDetail>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create(SaveAssignmentRequest request, CancellationToken ct) =>
        await ValidateAsync(request, ct) ?? From(await assignments.CreateAsync(request, ct));

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Assignments.Manage)]
    [ProducesResponseType<AssignmentDetail>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, SaveAssignmentRequest request, CancellationToken ct) =>
        await ValidateAsync(request, ct) ?? From(await assignments.UpdateAsync(id, request, ct));

    /// <summary>
    /// Moves the assignment: next stage, back one stage (with a reason), cancel (with a reason) or reopen.
    /// Open to the assigned valuer and reviewer as well as managers; the rules are checked per move.
    /// </summary>
    [HttpPost("{id:guid}/move")]
    [ProducesResponseType<AssignmentDetail>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Move(Guid id, MoveRequest request, CancellationToken ct) =>
        await ValidateAsync(request, ct) ?? From(await assignments.MoveAsync(id, request, ct));

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.Assignments.Manage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await assignments.DeleteAsync(id, ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error!);
    }
}
