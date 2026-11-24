using Microsoft.AspNetCore.Mvc;
using ValuPro.Api.Security;
using ValuPro.Application.Valuations;
using ValuPro.Domain.Security;
using ValuPro.Domain.Valuations;

namespace ValuPro.Api.Controllers;

/// <summary>Valuations, keyed by assignment id: fair market, realizable, distress, guideline and insurance values.</summary>
[Route("api/valuations")]
[HasPermission(Permissions.Valuation.View)]
public sealed class ValuationsController(IValuationService valuations) : ApiControllerBase
{
    /// <summary>Cases to value (inspection done, no valuation yet) or valued. Mine = only my cases.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ValuationListItem>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] ValuationQuery query, CancellationToken ct) => Ok(await valuations.ListAsync(query, ct));

    /// <summary>The saved valuation, or a starting point from the inspection, Rate Master and bank rules, with the rates on offer.</summary>
    [HttpGet("{assignmentId:guid}")]
    [ProducesResponseType<ValuationDetail>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid assignmentId, CancellationToken ct) => From(await valuations.GetAsync(assignmentId, ct));

    /// <summary>Works out the values for the given inputs without saving (called as the valuer types).</summary>
    [HttpPost("calculate")]
    [ProducesResponseType<ValuationResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Calculate(ValuationInput input, CancellationToken ct) =>
        await ValidateAsync(input, ct) ?? Ok(valuations.Calculate(input));

    [HttpPut("{assignmentId:guid}")]
    [HasPermission(Permissions.Valuation.Manage)]
    [ProducesResponseType<ValuationDetail>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Save(Guid assignmentId, SaveValuationRequest request, CancellationToken ct) =>
        await ValidateAsync(request, ct) ?? From(await valuations.SaveAsync(assignmentId, request, ct));
}
