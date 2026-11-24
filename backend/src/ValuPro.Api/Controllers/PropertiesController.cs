using Microsoft.AspNetCore.Mvc;
using ValuPro.Api.Security;
using ValuPro.Application.Common;
using ValuPro.Application.Properties;
using ValuPro.Domain.Security;

namespace ValuPro.Api.Controllers;

[Route("api/properties")]
[HasPermission(Permissions.Properties.View)]
public sealed class PropertiesController(IPropertyService properties) : ApiControllerBase
{
    /// <summary>Properties with search (code, owner, address, survey/CTS/plot, pincode), type/city/zone filters and paging.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<PropertyItem>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] PropertyQuery query, CancellationToken ct) =>
        await ValidateAsync(query, ct) ?? Ok(await properties.ListAsync(query, ct));

    /// <summary>Cities and zones in use (for filters), plus suggested zones.</summary>
    [HttpGet("filters")]
    [ProducesResponseType<PropertyFilters>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Filters(CancellationToken ct) => Ok(await properties.GetFiltersAsync(ct));

    /// <summary>The property with its assignment history.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<PropertyDetail>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => From(await properties.GetAsync(id, ct));

    [HttpPost]
    [HasPermission(Permissions.Properties.Manage)]
    [ProducesResponseType<PropertyItem>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create(SavePropertyRequest request, CancellationToken ct) =>
        await ValidateAsync(request, ct) ?? From(await properties.CreateAsync(request, ct));

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Properties.Manage)]
    [ProducesResponseType<PropertyItem>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, SavePropertyRequest request, CancellationToken ct) =>
        await ValidateAsync(request, ct) ?? From(await properties.UpdateAsync(id, request, ct));

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.Properties.Manage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await properties.DeleteAsync(id, ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error!);
    }
}
