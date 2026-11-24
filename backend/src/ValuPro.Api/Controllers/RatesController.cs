using System.Text;
using Microsoft.AspNetCore.Mvc;
using ValuPro.Api.Security;
using ValuPro.Application.Common;
using ValuPro.Application.Rates;
using ValuPro.Domain.Security;

namespace ValuPro.Api.Controllers;

/// <summary>Rate Master: market (location), circle / Jantri, construction rates and depreciation rules.</summary>
[Route("api/rates")]
[HasPermission(Permissions.RateMaster.View)]
public sealed class RatesController(IRateService rates) : ApiControllerBase
{
    [HttpGet("filters")]
    [ProducesResponseType<RateFilters>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Filters(CancellationToken ct) => Ok(await rates.GetFiltersAsync(ct));

    // ---- location ----
    [HttpGet("location")]
    [ProducesResponseType<PagedResult<LocationRateItem>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Location([FromQuery] RateQuery q, CancellationToken ct) =>
        await ValidateAsync(q, ct) ?? Ok(await rates.ListLocationAsync(q, ct));

    [HttpPost("location")]
    [HasPermission(Permissions.RateMaster.Manage)]
    public async Task<IActionResult> CreateLocation(SaveLocationRate r, CancellationToken ct) =>
        await ValidateAsync(r, ct) ?? From(await rates.SaveLocationAsync(null, r, ct));

    [HttpPut("location/{id:guid}")]
    [HasPermission(Permissions.RateMaster.Manage)]
    public async Task<IActionResult> UpdateLocation(Guid id, SaveLocationRate r, CancellationToken ct) =>
        await ValidateAsync(r, ct) ?? From(await rates.SaveLocationAsync(id, r, ct));

    // ---- circle ----
    [HttpGet("circle")]
    [ProducesResponseType<PagedResult<CircleRateItem>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Circle([FromQuery] RateQuery q, CancellationToken ct) =>
        await ValidateAsync(q, ct) ?? Ok(await rates.ListCircleAsync(q, ct));

    [HttpPost("circle")]
    [HasPermission(Permissions.RateMaster.Manage)]
    public async Task<IActionResult> CreateCircle(SaveCircleRate r, CancellationToken ct) =>
        await ValidateAsync(r, ct) ?? From(await rates.SaveCircleAsync(null, r, ct));

    [HttpPut("circle/{id:guid}")]
    [HasPermission(Permissions.RateMaster.Manage)]
    public async Task<IActionResult> UpdateCircle(Guid id, SaveCircleRate r, CancellationToken ct) =>
        await ValidateAsync(r, ct) ?? From(await rates.SaveCircleAsync(id, r, ct));

    // ---- construction ----
    [HttpGet("construction")]
    [ProducesResponseType<PagedResult<ConstructionRateItem>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Construction([FromQuery] RateQuery q, CancellationToken ct) =>
        await ValidateAsync(q, ct) ?? Ok(await rates.ListConstructionAsync(q, ct));

    [HttpPost("construction")]
    [HasPermission(Permissions.RateMaster.Manage)]
    public async Task<IActionResult> CreateConstruction(SaveConstructionRate r, CancellationToken ct) =>
        await ValidateAsync(r, ct) ?? From(await rates.SaveConstructionAsync(null, r, ct));

    [HttpPut("construction/{id:guid}")]
    [HasPermission(Permissions.RateMaster.Manage)]
    public async Task<IActionResult> UpdateConstruction(Guid id, SaveConstructionRate r, CancellationToken ct) =>
        await ValidateAsync(r, ct) ?? From(await rates.SaveConstructionAsync(id, r, ct));

    // ---- depreciation ----
    [HttpGet("depreciation")]
    [ProducesResponseType<IReadOnlyList<DepreciationRuleItem>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Depreciation(CancellationToken ct) => Ok(await rates.ListDepreciationAsync(ct));

    [HttpPost("depreciation")]
    [HasPermission(Permissions.RateMaster.Manage)]
    public async Task<IActionResult> CreateDepreciation(SaveDepreciationRule r, CancellationToken ct) =>
        await ValidateAsync(r, ct) ?? From(await rates.SaveDepreciationAsync(null, r, ct));

    [HttpPut("depreciation/{id:guid}")]
    [HasPermission(Permissions.RateMaster.Manage)]
    public async Task<IActionResult> UpdateDepreciation(Guid id, SaveDepreciationRule r, CancellationToken ct) =>
        await ValidateAsync(r, ct) ?? From(await rates.SaveDepreciationAsync(id, r, ct));

    /// <summary>Adds RCC framed, load bearing, steel shed and temporary structure rules that are missing.</summary>
    [HttpPost("depreciation/standard")]
    [HasPermission(Permissions.RateMaster.Manage)]
    public async Task<IActionResult> AddStandard(CancellationToken ct) => Ok(await rates.AddStandardDepreciationAsync(ct));

    // ---- delete (any kind) ----
    [HttpDelete("{kind}/{id:guid}")]
    [HasPermission(Permissions.RateMaster.Manage)]
    public async Task<IActionResult> Delete(string kind, Guid id, CancellationToken ct)
    {
        var result = await rates.DeleteAsync(kind, id, ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error!);
    }

    // ---- import ----
    [HttpGet("{kind}/import/template")]
    [HasPermission(Permissions.RateMaster.Manage)]
    public IActionResult Template(RateKind kind) =>
        File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(rates.Template(kind))).ToArray(), "text/csv",
            $"valupro-{kind.ToString().ToLowerInvariant()}-rates-template.csv");

    /// <summary>Checks every row of a .csv / .xlsx file without saving.</summary>
    [HttpPost("{kind}/import/preview")]
    [HasPermission(Permissions.RateMaster.Manage)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> Preview(RateKind kind, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return Problem(Error.Validation("Choose a .csv or .xlsx file.", "file"));
        await using var s = file.OpenReadStream();
        return From(await rates.PreviewImportAsync(kind, s, file.FileName, ct));
    }

    /// <summary>Imports the valid rows of the same file; rows with problems are skipped.</summary>
    [HttpPost("{kind}/import")]
    [HasPermission(Permissions.RateMaster.Manage)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> Import(RateKind kind, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return Problem(Error.Validation("Choose a .csv or .xlsx file.", "file"));
        await using var s = file.OpenReadStream();
        return From(await rates.ImportAsync(kind, s, file.FileName, ct));
    }
}
